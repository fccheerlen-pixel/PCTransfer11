using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using PCTransfer11.Models;

namespace PCTransfer11.Services;

/// <summary>
/// Zet een PCTransfer11-back-up (een map met een manifest.json erin, zoals
/// gemaakt door PackageBuilder) terug op deze pc. Kan alles terugzetten, of
/// alleen een door de gebruiker gekozen selectie (bv. alleen "Afbeeldingen").
/// </summary>
public sealed class PackageRestorer
{
    private readonly IProgress<string> _log;

    public PackageRestorer(IProgress<string> log)
    {
        _log = log;
    }

    /// <summary>
    /// Leest het manifest.json in de opgegeven back-upmap in, zodat de UI kan
    /// tonen wat er in de back-up zit vóórdat er iets wordt teruggezet.
    /// </summary>
    public static PackageManifest LoadManifest(string backupFolderPath)
    {
        string manifestPath = Path.Combine(backupFolderPath, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("Dit is geen geldige PCTransfer11-back-upmap (manifest.json ontbreekt).");

        string json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<PackageManifest>(json)
               ?? throw new InvalidOperationException("Het manifest kon niet worden gelezen.");
    }

    /// <summary>
    /// Volledig terugzetten vanaf een ontvangen .pctbackup-zipbestand
    /// (gebruikt na rechtstreekse netwerkoverdracht).
    /// </summary>
    public async Task RestoreZipAsync(string packageZipPath, bool overwriteExisting, IProgress<double>? percent, CancellationToken ct)
    {
        string stagingDir = Path.Combine(Path.GetTempPath(), "PCTransfer11_restore_" + Guid.NewGuid().ToString("N"));
        try
        {
            _log.Report("Pakket uitpakken ...");
            ZipFile.ExtractToDirectory(packageZipPath, stagingDir);

            var manifest = LoadManifest(stagingDir);
            await RestoreFromFolderAsync(stagingDir, manifest, filePackagePaths: null, settingsAppIds: null, overwriteExisting, percent, ct);
        }
        finally
        {
            TryDeleteDirectory(stagingDir);
        }
    }

    /// <summary>
    /// Zet (een selectie van) een back-upmap terug op deze pc.
    /// <paramref name="filePackagePaths"/> en <paramref name="settingsAppIds"/> geven aan
    /// welke items teruggezet moeten worden (vergelijk met FileEntry.PackagePath resp.
    /// SettingsEntry.AppId uit het manifest) - geef <c>null</c> mee om alles terug te zetten.
    /// </summary>
    public async Task RestoreFromFolderAsync(
        string backupFolderPath,
        PackageManifest manifest,
        ISet<string>? filePackagePaths,
        ISet<string>? settingsAppIds,
        bool overwriteExisting,
        IProgress<double>? percent,
        CancellationToken ct)
    {
        _log.Report($"Back-up gemaakt op '{manifest.CreatedByMachine}' ({manifest.CreatedAtUtc:g} UTC).");

        var filesToRestore = manifest.Files
            .Where(f => filePackagePaths == null || filePackagePaths.Contains(f.PackagePath))
            .ToList();
        var settingsToRestore = manifest.Settings
            .Where(s => settingsAppIds == null || settingsAppIds.Contains(s.AppId))
            .ToList();

        int totalSteps = Math.Max(1, filesToRestore.Count + settingsToRestore.Count);
        int doneSteps = 0;
        void ReportPercent()
        {
            doneSteps++;
            percent?.Report((double)doneSteps / totalSteps);
        }

        foreach (var fileEntry in filesToRestore)
        {
            ct.ThrowIfCancellationRequested();
            string source = Path.Combine(backupFolderPath, fileEntry.PackagePath);
            if (!Directory.Exists(source) && !File.Exists(source))
            {
                _log.Report($"Overslaan, ontbreekt in back-up: {fileEntry.DisplayName}");
                ReportPercent();
                continue;
            }

            _log.Report($"Terugzetten: {fileEntry.DisplayName} -> {fileEntry.OriginalPath}");
            try
            {
                if (Directory.Exists(source))
                    await CopyDirectoryAsync(source, fileEntry.OriginalPath, overwriteExisting, ct);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fileEntry.OriginalPath)!);
                    File.Copy(source, fileEntry.OriginalPath, overwriteExisting);
                }
            }
            catch (Exception ex)
            {
                _log.Report($"Fout bij terugzetten van '{fileEntry.DisplayName}': {ex.Message}");
            }
            ReportPercent();
        }

        foreach (var settingsEntry in settingsToRestore)
        {
            ct.ThrowIfCancellationRequested();
            var app = KnownApps.GetAll().FirstOrDefault(a => a.Id == settingsEntry.AppId);

            if (settingsEntry.HasDataFolder)
            {
                string dataSource = Path.Combine(backupFolderPath, PackageBuilder.SettingsFolderName, settingsEntry.AppId, "data");
                string? dataDestination = app?.ResolveDataFolder();
                if (dataDestination == null)
                {
                    _log.Report($"Kan doelmap voor '{settingsEntry.DisplayName}' niet bepalen op deze pc " +
                                "(applicatie waarschijnlijk niet geïnstalleerd) - overgeslagen.");
                }
                else
                {
                    _log.Report($"Instellingen terugzetten: {settingsEntry.DisplayName} ...");
                    await CopyDirectoryAsync(dataSource, dataDestination, overwriteExisting, ct);
                }
            }

            if (settingsEntry.HasRegistryExport)
            {
                string regFile = Path.Combine(backupFolderPath, PackageBuilder.SettingsFolderName, settingsEntry.AppId, "registry.reg");
                if (File.Exists(regFile))
                {
                    _log.Report($"Registerinstellingen terugzetten: {settingsEntry.DisplayName} ...");
                    await ImportRegistryFileAsync(regFile, ct);
                }
            }
            ReportPercent();
        }

        percent?.Report(1.0);
        _log.Report("Terugzetten voltooid.");
    }

    private async Task CopyDirectoryAsync(string sourceDir, string destinationDir, bool overwrite, CancellationToken ct)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
        }
        foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string dest = filePath.Replace(sourceDir, destinationDir);
            if (!overwrite && File.Exists(dest))
                continue;
            try { await CopyFileStreamedAsync(filePath, dest, ct); }
            catch (OperationCanceledException) { throw; }
            catch (IOException) { /* bestand in gebruik - overslaan */ }
            catch (UnauthorizedAccessException) { /* geen toegang - overslaan */ }
        }
    }

    /// <summary>
    /// Kopieert één bestand met een handmatige stream-loop (i.p.v. File.Copy)
    /// zodat het CancellationToken tijdens het kopiëren zelf gecontroleerd
    /// wordt - de "Stop"-knop reageert zo ook meteen bij grote bestanden.
    /// </summary>
    private static async Task CopyFileStreamedAsync(string sourceFile, string destFile, CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024; // 1 MB
        string? destDir = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        await using var src = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var dst = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        var buffer = new byte[bufferSize];
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
    }

    /// <summary>
    /// Importeert een .reg-bestand met het ingebouwde Windows-hulpprogramma
    /// reg.exe. Werkt alleen op HKEY_CURRENT_USER-sleutels (die vereisen
    /// geen adminrechten), zoals ook alleen HKCU wordt geëxporteerd.
    /// </summary>
    private async Task ImportRegistryFileAsync(string regFile, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"import \"{regFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return;
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
                _log.Report("Waarschuwing: het importeren van de registerinstellingen is mogelijk niet volledig gelukt.");
        }
        catch (Exception ex)
        {
            _log.Report($"Kon registerbestand niet importeren: {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort opruimen van tijdelijke bestanden */ }
    }
}
