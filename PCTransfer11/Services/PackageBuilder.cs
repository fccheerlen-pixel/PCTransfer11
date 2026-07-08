using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using PCTransfer11.Models;

namespace PCTransfer11.Services;

/// <summary>
/// Bouwt een PCTransfer11-back-up op basis van de door de gebruiker
/// geselecteerde bestanden/mappen en applicatie-instellingen.
///
/// De back-up wordt als GEWONE MAP weggeschreven (elk geselecteerd item
/// krijgt zijn eigen submap met de herkenbare naam, bv. "Documenten",
/// "Afbeeldingen"), zodat de gebruiker de back-up rechtstreeks in
/// Verkenner kan openen, bekijken en bewerken. Er staat een manifest.json
/// naast, die onthoudt welke map bij welk oorspronkelijk pad hoort zodat
/// alles later weer op de juiste plek teruggezet kan worden.
///
/// Voor netwerkoverdracht (waar één stroom bytes nodig is) wordt dezelfde
/// mapstructuur eerst in een tijdelijke map gebouwd en daarna pas ingepakt
/// tot één zip-bestand (zie BuildToZipAsync).
///
/// Er wordt eerst een "pre-scan" gedaan (grootte van alles optellen) zodat
/// de voortgangsbalk een écht percentage kan tonen in plaats van alleen
/// "bezig". Bestanden die alleen online staan (OneDrive-placeholders e.d.)
/// worden gedetecteerd en overgeslagen in plaats van geprobeerd te
/// downloaden - dat laatste kon de app minutenlang laten "vastlopen" als
/// het bestand groot was of de internetverbinding traag.
/// </summary>
public sealed class PackageBuilder
{
    private readonly IProgress<string> _log;

    /// <summary>
    /// Volledig, genormaliseerd pad van de back-updoelmap voor de huidige build,
    /// gebruikt door de runtime-guard in CopyDirectoryRecursiveAsync (tweede
    /// verdedigingslinie tegen zichzelf-in-zichzelf kopiëren). Wordt gezet aan
    /// het begin van BuildToDirectoryAsync.
    /// </summary>
    private string? _guardOutputRoot;

    public PackageBuilder(IProgress<string> log)
    {
        _log = log;
    }

    /// <summary>
    /// Bouwt de back-up rechtstreeks in <paramref name="outputDirectory"/> (wordt
    /// aangemaakt als hij nog niet bestaat). Dit is de map die de gebruiker
    /// straks kan openen/bekijken/bewerken, en die later weer gekozen kan
    /// worden om (een selectie van) terug te zetten.
    /// </summary>
    public async Task<string> BuildToDirectoryAsync(
        IEnumerable<FileSelectionItem> selectedFiles,
        IEnumerable<AppProfile> selectedApps,
        string outputDirectory,
        IProgress<double>? percentProgress,
        CancellationToken ct)
    {
        var filesList = selectedFiles.Where(f => f.Exists).ToList();
        var appsList = selectedApps.ToList();

        // ---- Veiligheidscontrole: voorkom dat de back-upmap in zichzelf terechtkomt ----
        // Als de gekozen doelmap gelijk is aan, of ligt binnen, één van de mappen
        // die wordt gebackupt (bv. de doelmap staat ergens onder "Documenten"
        // terwijl "Documenten" zelf ook wordt gebackupt), dan komt het kopiëren
        // van die bronmap de zojuist aangemaakte back-upmap weer tegen als
        // "nieuwe inhoud" en kopieert hij zichzelf record voor record naar
        // zichzelf, tot de padlengte van Windows (260 tekens) geraakt wordt en
        // alles vastloopt. Dit vooraf blokkeren voorkomt die oneindige lus
        // helemaal, in plaats van de gebruiker pas te laten crashen. Dit gebeurt
        // bewust vóórdat de doelmap wordt aangemaakt, zodat er ook geen
        // (mogelijk al geneste) map wordt achtergelaten als we alsnog weigeren.
        string? conflictingSource = FindNestingConflict(outputDirectory, filesList, appsList);
        if (conflictingSource != null)
        {
            throw new InvalidOperationException(
                $"De gekozen back-upbestemming ligt binnen (of is gelijk aan) de map '{conflictingSource}', " +
                "die zelf ook wordt gebackupt. Kies een bestemming buiten de mappen die je back-upt.");
        }

        // Onthouden voor de runtime-guard tijdens het kopiëren zelf (tweede
        // verdedigingslinie, voor het geval een pad via een andere route dan
        // hierboven alsnog binnen een bronmap terecht zou komen).
        _guardOutputRoot = NormalizeFullPath(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        // ---- Pre-scan: grootte bepalen vóórdat er iets gekopieerd wordt ----
        _log.Report("Bestanden tellen en grootte bepalen ...");
        long totalBytes = 0;
        int cloudFilesFound = 0;
        foreach (var item in filesList)
            totalBytes += ScanSize(item.Path, ct, ref cloudFilesFound);
        foreach (var app in appsList)
        {
            string? dataFolder = app.ResolveDataFolder();
            if (dataFolder != null && Directory.Exists(dataFolder))
                totalBytes += ScanSize(dataFolder, ct, ref cloudFilesFound);
        }

        _log.Report($"{FormatBytes(totalBytes)} te kopiëren.");
        if (cloudFilesFound > 0)
            _log.Report($"Let op: {cloudFilesFound} bestand(en) staan alleen online (bv. OneDrive 'alleen-online') " +
                        "en worden overgeslagen om vastlopen te voorkomen. Download ze eerst lokaal als je ze wel mee wil nemen.");

        var tracker = new ByteProgressTracker(totalBytes, percentProgress);

        var manifest = new PackageManifest();
        // "manifest.json" en de instellingenmap zijn gereserveerde namen binnen een back-up;
        // een geselecteerde map met toevallig dezelfde naam krijgt zo een " (2)" achtervoegsel.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json", SettingsFolderName };
        string settingsRoot = Path.Combine(outputDirectory, SettingsFolderName);

        foreach (var item in filesList)
        {
            ct.ThrowIfCancellationRequested();

            string safeName = MakeUniqueName(SanitizeForFileName(item.DisplayName), usedNames);
            string destination = Path.Combine(outputDirectory, safeName);

            _log.Report($"Bestanden kopiëren: {item.DisplayName} ...");
            if (Directory.Exists(item.Path))
                await CopyDirectoryAsync(item.Path, destination, tracker, ct);
            else
                await CopyFileTrackedAsync(item.Path, destination, tracker, ct);

            manifest.Files.Add(new PackageManifest.FileEntry
            {
                PackagePath = safeName,
                OriginalPath = item.Path,
                DisplayName = item.DisplayName
            });
        }

        foreach (var app in appsList)
        {
            ct.ThrowIfCancellationRequested();
            string appStagingDir = Path.Combine(settingsRoot, app.Id);

            var entry = new PackageManifest.SettingsEntry
            {
                AppId = app.Id,
                DisplayName = app.DisplayName,
                RegistryKey = app.RegistryKeys != null ? string.Join("; ", app.RegistryKeys) : null
            };

            bool hasAnySource = false;

            string? dataFolder = app.ResolveDataFolder();
            if (dataFolder != null && Directory.Exists(dataFolder))
            {
                _log.Report($"Instellingen kopiëren: {app.DisplayName} ...");
                Directory.CreateDirectory(appStagingDir);
                string dataDestination = Path.Combine(appStagingDir, "data");
                await CopyDirectoryAsync(dataFolder, dataDestination, tracker, ct);
                entry.HasDataFolder = true;
                hasAnySource = true;
            }

            if (app.RegistryKeys is { Length: > 0 })
            {
                _log.Report($"Registerinstellingen exporteren: {app.DisplayName} ...");
                Directory.CreateDirectory(appStagingDir);
                bool anyOk = false;
                for (int i = 0; i < app.RegistryKeys.Length; i++)
                {
                    string regFile = Path.Combine(appStagingDir, $"registry_{i}.reg");
                    if (await ExportRegistryKeyAsync(app.RegistryKeys[i], regFile, ct))
                        anyOk = true;
                }
                entry.HasRegistryExport = anyOk;
                hasAnySource = hasAnySource || anyOk;
            }

            if (app.CustomExport != null)
            {
                _log.Report($"Instellingen ophalen: {app.DisplayName} ...");
                Directory.CreateDirectory(appStagingDir);
                string customDestination = Path.Combine(appStagingDir, "data");
                bool ok = await app.CustomExport(customDestination, ct, _log);
                entry.HasCustomData = ok;
                entry.HasDataFolder = entry.HasDataFolder || ok; // hergebruikt hetzelfde "data"-pad bij terugzetten
                hasAnySource = hasAnySource || ok;
            }

            if (!hasAnySource)
            {
                _log.Report($"Overslaan (niet gevonden op dit systeem): {app.DisplayName}");
                continue;
            }

            manifest.Settings.Add(entry);
        }

        string manifestPath = Path.Combine(outputDirectory, "manifest.json");
        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

        percentProgress?.Report(1.0);
        _log.Report($"Back-up klaar in map: {outputDirectory}");
        return outputDirectory;
    }

    /// <summary>
    /// Bouwt dezelfde back-up als BuildToDirectoryAsync, maar dan in een
    /// tijdelijke map die daarna wordt ingepakt tot één .pctbackup-zipbestand.
    /// Wordt alleen gebruikt voor rechtstreekse netwerkoverdracht, waar één
    /// stroom bytes nodig is; de tijdelijke map wordt achteraf opgeruimd.
    /// </summary>
    public async Task<string> BuildToZipAsync(
        IEnumerable<FileSelectionItem> selectedFiles,
        IEnumerable<AppProfile> selectedApps,
        string outputZipPath,
        CancellationToken ct)
    {
        string stagingDir = Path.Combine(Path.GetTempPath(), "PCTransfer11_" + Guid.NewGuid().ToString("N"));
        try
        {
            await BuildToDirectoryAsync(selectedFiles, selectedApps, stagingDir, percentProgress: null, ct);

            _log.Report("Pakket comprimeren voor verzending ...");
            if (File.Exists(outputZipPath))
                File.Delete(outputZipPath);
            ZipFile.CreateFromDirectory(stagingDir, outputZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            _log.Report($"Pakket klaar: {outputZipPath}");
            return outputZipPath;
        }
        finally
        {
            TryDeleteDirectory(stagingDir);
        }
    }

    /// <summary>Naam van de submap waarin app-instellingen worden bewaard binnen een back-up.</summary>
    public const string SettingsFolderName = "_instellingen";

    // ---------------------------------------------------------------------
    // Pre-scan (grootte bepalen + cloud-only bestanden detecteren)
    // ---------------------------------------------------------------------

    private long ScanSize(string path, CancellationToken ct, ref int cloudFilesFound)
    {
        ct.ThrowIfCancellationRequested();

        if (File.Exists(path))
        {
            if (IsCloudPlaceholder(path))
            {
                cloudFilesFound++;
                return 0; // wordt straks overgeslagen, telt niet mee voor de voortgang
            }
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        if (!Directory.Exists(path))
            return 0;

        var dirInfo = new DirectoryInfo(path);
        if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return 0; // junction/symlink: zelfde reden als bij het kopiëren zelf overgeslagen

        long total = 0;
        int localCloudCount = 0;

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(path))
            {
                ct.ThrowIfCancellationRequested();
                if (IsCloudPlaceholder(filePath)) { localCloudCount++; continue; }
                try { total += new FileInfo(filePath).Length; } catch { /* ontoegankelijk - negeren tijdens scan */ }
            }

            foreach (string subDir in Directory.EnumerateDirectories(path))
                total += ScanSize(subDir, ct, ref cloudFilesFound);
        }
        catch (UnauthorizedAccessException) { /* geen toegang - overslaan tijdens scan, kopieerstap doet dit ook */ }

        cloudFilesFound += localCloudCount;
        return total;
    }

    /// <summary>
    /// Herkent bestanden die door OneDrive/andere cloudsync als "alleen online"
    /// zijn gemarkeerd (niet lokaal gedownload). Die openen/lezen kan Windows
    /// dwingen om ze eerst te downloaden, wat bij grote bestanden of een
    /// trage verbinding de app minutenlang kan laten hangen. Deze vlaggen
    /// zitten niet in het standaard System.IO.FileAttributes-enum, maar wel
    /// in de onderliggende Win32-waarde die File.GetAttributes teruggeeft.
    /// </summary>
    private static bool IsCloudPlaceholder(string filePath)
    {
        const int FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;
        const int FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000;

        try
        {
            int attrs = (int)File.GetAttributes(filePath);
            return (attrs & FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0
                || (attrs & FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0
                || (attrs & (int)FileAttributes.Offline) != 0;
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------------
    // Kopiëren (met byte-niveau voortgang en snelle annulering)
    // ---------------------------------------------------------------------

    private async Task CopyDirectoryAsync(string sourceDir, string destinationDir, ByteProgressTracker tracker, CancellationToken ct)
    {
        await CopyDirectoryRecursiveAsync(sourceDir, destinationDir, tracker, ct);
    }

    /// <summary>
    /// Kopieert een map recursief, maar slaat reparse points (junctions/symlinks)
    /// over. Windows gebruikt zulke junctions voor legacy-mappen als
    /// "Documenten\Mijn afbeeldingen" die eigenlijk naar elders verwijzen; direct
    /// benaderen daarvan geeft altijd "Access denied" voor niet-Verkenner-
    /// processen. De echte doelmap wordt sowieso al los meegenomen als die apart
    /// in de selectie staat, dus overslaan hier verliest geen data.
    /// </summary>
    private async Task CopyDirectoryRecursiveAsync(string sourceDir, string destinationDir, ByteProgressTracker tracker, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dirInfo = new DirectoryInfo(sourceDir);
        if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            // Junction/symlink: overslaan om Access-denied te voorkomen.
            return;
        }

        Directory.CreateDirectory(destinationDir);

        foreach (string filePath in Directory.EnumerateFiles(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            string destFile = Path.Combine(destinationDir, Path.GetFileName(filePath));
            try
            {
                await CopyFileTrackedAsync(filePath, destFile, tracker, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException)
            {
                // Bestand is in gebruik door een lopende applicatie (bv. een browser-databasebestand) - overslaan.
                _log.Report($"Overgeslagen (in gebruik door andere app): {filePath}");
            }
            catch (UnauthorizedAccessException)
            {
                // Geen toegang (bv. systeembestand) - overslaan.
                _log.Report($"Overgeslagen (geen toegang): {filePath}");
            }
        }

        foreach (string subDir in Directory.EnumerateDirectories(sourceDir))
        {
            ct.ThrowIfCancellationRequested();

            // Runtime-guard (tweede verdedigingslinie naast de pre-check in
            // BuildToDirectoryAsync): als deze submap zelf de back-updoelmap is,
            // of die bevat, dan zou verder afdalen de back-up weer in zichzelf
            // gaan kopiëren en oneindig doorlopen tot Windows' padlengtelimiet.
            // We slaan alleen déze ene submap over; de rest van de bronmap wordt
            // gewoon normaal meegenomen.
            if (_guardOutputRoot != null && IsSameOrNestedUnder(_guardOutputRoot, subDir))
            {
                _log.Report($"Overgeslagen (dit is de back-upmap zelf, zou oneindig in zichzelf kopiëren): {subDir}");
                continue;
            }

            try
            {
                string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                await CopyDirectoryRecursiveAsync(subDir, destSubDir, tracker, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException)
            {
                // Geen toegang tot deze submap - overslaan en doorgaan met de rest.
                _log.Report($"Overgeslagen (geen toegang): {subDir}");
            }
        }
    }

    /// <summary>
    /// Kopieert één bestand met een handmatige, gebufferde stream-loop in
    /// plaats van File.Copy. Dit heeft twee voordelen: (1) elke leesactie
    /// controleert het CancellationToken, dus de "Stop"-knop reageert ook
    /// meteen tijdens het kopiëren van een groot bestand (bv. een video van
    /// enkele GB's) in plaats van pas nadat dat ene bestand klaar is; en
    /// (2) er kan per gekopieerd blok voortgang worden gerapporteerd voor
    /// een vloeiende, betrouwbare voortgangsbalk.
    /// </summary>
    private async Task CopyFileTrackedAsync(string sourceFile, string destFile, ByteProgressTracker tracker, CancellationToken ct)
    {
        if (IsCloudPlaceholder(sourceFile))
        {
            _log.Report($"Overgeslagen (alleen online beschikbaar): {sourceFile}");
            return;
        }

        const int bufferSize = 1024 * 1024; // 1 MB
        string? destDir = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        await using var src = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var dst = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        var buffer = new byte[bufferSize];
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            tracker.AddCopiedBytes(read);
        }
    }

    /// <summary>
    /// Houdt de totale voortgang (in bytes) bij en stuurt die - afgeremd tot
    /// ~10x per seconde, zodat de UI-thread niet overspoeld wordt - door
    /// naar de voortgangsbalk als percentage.
    /// </summary>
    private sealed class ByteProgressTracker
    {
        private readonly long _totalBytes;
        private readonly IProgress<double>? _percentProgress;
        private long _doneBytes;
        private long _lastReportTicks;

        public ByteProgressTracker(long totalBytes, IProgress<double>? percentProgress)
        {
            _totalBytes = Math.Max(1, totalBytes);
            _percentProgress = percentProgress;
        }

        public void AddCopiedBytes(long bytes)
        {
            long done = Interlocked.Add(ref _doneBytes, bytes);
            long nowTicks = DateTime.UtcNow.Ticks;
            long lastTicks = Interlocked.Read(ref _lastReportTicks);
            // Max. ~10 updates/seconde, plus altijd de allerlaatste.
            if (nowTicks - lastTicks < TimeSpan.TicksPerMillisecond * 100 && done < _totalBytes)
                return;
            Interlocked.Exchange(ref _lastReportTicks, nowTicks);
            _percentProgress?.Report(Math.Min(1.0, (double)done / _totalBytes));
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    /// <summary>
    /// Exporteert een registersleutel met het ingebouwde Windows-hulpprogramma
    /// reg.exe (standaard onderdeel van Windows, geen extra installatie nodig).
    /// </summary>
    private async Task<bool> ExportRegistryKeyAsync(string registryKey, string outputRegFile, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"export \"{registryKey}\" \"{outputRegFile}\" /y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0 && File.Exists(outputRegFile);
        }
        catch (Exception ex)
        {
            _log.Report($"Kon registersleutel niet exporteren ({registryKey}): {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------------
    // Bescherming tegen een back-upbestemming die (deels) samenvalt met een bron
    // ---------------------------------------------------------------------

    private static string NormalizeFullPath(string path)
    {
        string full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// True als <paramref name="candidate"/> hetzelfde pad is als, of een
    /// (klein)kind-map is van, <paramref name="ancestor"/>. Vergelijkt op basis
    /// van volledige, genormaliseerde paden zodat een afwijkend hoofdlettergebruik
    /// of een afsluitende backslash geen vals-negatief resultaat geven.
    /// </summary>
    private static bool IsSameOrNestedUnder(string candidate, string ancestor)
    {
        string a = NormalizeFullPath(candidate);
        string b = NormalizeFullPath(ancestor);

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        string bWithSeparator = b + Path.DirectorySeparatorChar;
        return a.StartsWith(bWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zoekt of de gekozen back-upbestemming gelijk is aan, of ligt binnen, één
    /// van de geselecteerde bronmappen (los geselecteerde mappen, of de
    /// datamappen van geselecteerde apps). Geeft het conflicterende bronpad
    /// terug, of null als er geen overlap is.
    /// </summary>
    private static string? FindNestingConflict(
        string outputDirectory,
        List<FileSelectionItem> filesList,
        List<AppProfile> appsList)
    {
        foreach (var item in filesList)
        {
            if (Directory.Exists(item.Path) && IsSameOrNestedUnder(outputDirectory, item.Path))
                return item.Path;
        }

        foreach (var app in appsList)
        {
            string? dataFolder = app.ResolveDataFolder();
            if (dataFolder != null && Directory.Exists(dataFolder) && IsSameOrNestedUnder(outputDirectory, dataFolder))
                return dataFolder;
        }

        return null;
    }

    private static string SanitizeForFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    /// <summary>
    /// Zorgt dat twee geselecteerde items nooit dezelfde mapnaam in de back-up
    /// krijgen (bv. twee zelf toegevoegde mappen die toevallig hetzelfde heten).
    /// </summary>
    private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
    {
        string candidate = baseName;
        int suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName} ({suffix})";
            suffix++;
        }
        return candidate;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort opruimen van tijdelijke bestanden */ }
    }
}
