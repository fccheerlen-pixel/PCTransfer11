using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using PCTransfer11.Models;

namespace PCTransfer11.Services;

/// <summary>
/// Bouwt een enkel .pctbackup-pakket (een gewoon zip-bestand met een
/// manifest.json erin) op basis van de door de gebruiker geselecteerde
/// bestanden/mappen en applicatie-instellingen. Dit pakket wordt daarna
/// óf rechtstreeks op schijf bewaard (back-upbestand-modus), óf via het
/// netwerk naar een andere pc gestuurd (netwerkmodus) - de opbouw is in
/// beide gevallen identiek.
/// </summary>
public sealed class PackageBuilder
{
    private readonly IProgress<string> _log;

    public PackageBuilder(IProgress<string> log)
    {
        _log = log;
    }

    public async Task<string> BuildAsync(
        IEnumerable<FileSelectionItem> selectedFiles,
        IEnumerable<AppProfile> selectedApps,
        string outputZipPath,
        CancellationToken ct)
    {
        string stagingDir = Path.Combine(Path.GetTempPath(), "PCTransfer11_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        string filesRoot = Path.Combine(stagingDir, "files");
        string settingsRoot = Path.Combine(stagingDir, "settings");
        Directory.CreateDirectory(filesRoot);
        Directory.CreateDirectory(settingsRoot);

        var manifest = new PackageManifest();

        try
        {
            int fileIndex = 0;
            foreach (var item in selectedFiles)
            {
                ct.ThrowIfCancellationRequested();
                if (!item.Exists)
                {
                    _log.Report($"Overslaan (bestaat niet): {item.DisplayName}");
                    continue;
                }

                fileIndex++;
                string safeName = $"{fileIndex:D2}_{SanitizeForFileName(item.DisplayName)}";
                string destination = Path.Combine(filesRoot, safeName);

                _log.Report($"Bestanden kopiëren: {item.DisplayName} ...");
                if (Directory.Exists(item.Path))
                    await CopyDirectoryAsync(item.Path, destination, ct);
                else
                    File.Copy(item.Path, destination);

                manifest.Files.Add(new PackageManifest.FileEntry
                {
                    PackagePath = "files/" + safeName,
                    OriginalPath = item.Path,
                    DisplayName = item.DisplayName
                });
            }

            foreach (var app in selectedApps)
            {
                ct.ThrowIfCancellationRequested();
                string appStagingDir = Path.Combine(settingsRoot, app.Id);
                Directory.CreateDirectory(appStagingDir);

                var entry = new PackageManifest.SettingsEntry
                {
                    AppId = app.Id,
                    DisplayName = app.DisplayName,
                    RegistryKey = app.RegistryKey
                };

                string? dataFolder = app.ResolveDataFolder();
                if (dataFolder != null && Directory.Exists(dataFolder))
                {
                    _log.Report($"Instellingen kopiëren: {app.DisplayName} ...");
                    string dataDestination = Path.Combine(appStagingDir, "data");
                    await CopyDirectoryAsync(dataFolder, dataDestination, ct);
                    entry.HasDataFolder = true;
                }
                else if (dataFolder == null && app.RegistryKey == null)
                {
                    _log.Report($"Overslaan (niet gevonden op dit systeem): {app.DisplayName}");
                    continue;
                }

                if (app.RegistryKey != null)
                {
                    _log.Report($"Registerinstellingen exporteren: {app.DisplayName} ...");
                    string regFile = Path.Combine(appStagingDir, "registry.reg");
                    bool ok = await ExportRegistryKeyAsync(app.RegistryKey, regFile, ct);
                    entry.HasRegistryExport = ok;
                }

                if (entry.HasDataFolder || entry.HasRegistryExport)
                    manifest.Settings.Add(entry);
            }

            string manifestPath = Path.Combine(stagingDir, "manifest.json");
            string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

            _log.Report("Pakket comprimeren tot back-upbestand ...");
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

    private async Task CopyDirectoryAsync(string sourceDir, string destinationDir, CancellationToken ct)
    {
        await Task.Run(() =>
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
                try
                {
                    File.Copy(filePath, filePath.Replace(sourceDir, destinationDir), overwrite: true);
                }
                catch (IOException)
                {
                    // Bestand is in gebruik door een lopende applicatie (bv. een browser-databasebestand) - overslaan.
                }
                catch (UnauthorizedAccessException)
                {
                    // Geen toegang (bv. systeembestand) - overslaan.
                }
            }
        }, ct);
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

    private static string SanitizeForFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort opruimen van tijdelijke bestanden */ }
    }
}
