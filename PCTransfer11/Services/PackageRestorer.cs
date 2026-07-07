using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using PCTransfer11.Models;

namespace PCTransfer11.Services;

/// <summary>
/// Pakt een .pctbackup-pakket uit en zet de bestanden en instellingen terug
/// op deze pc. Wordt gebruikt na het inlezen van een back-upbestand, en ook
/// nadat een pakket via het netwerk is ontvangen (zelfde pakketformaat).
/// </summary>
public sealed class PackageRestorer
{
    private readonly IProgress<string> _log;

    public PackageRestorer(IProgress<string> log)
    {
        _log = log;
    }

    public async Task RestoreAsync(string packageZipPath, bool overwriteExisting, CancellationToken ct)
    {
        string stagingDir = Path.Combine(Path.GetTempPath(), "PCTransfer11_restore_" + Guid.NewGuid().ToString("N"));
        try
        {
            _log.Report("Pakket uitpakken ...");
            ZipFile.ExtractToDirectory(packageZipPath, stagingDir);

            string manifestPath = Path.Combine(stagingDir, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("Dit is geen geldig PCTransfer11-pakket (manifest.json ontbreekt).");

            var manifest = JsonSerializer.Deserialize<PackageManifest>(await File.ReadAllTextAsync(manifestPath, ct))
                            ?? throw new InvalidOperationException("Het manifest kon niet worden gelezen.");

            _log.Report($"Pakket gemaakt op '{manifest.CreatedByMachine}' ({manifest.CreatedAtUtc:g} UTC).");

            foreach (var fileEntry in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                string source = Path.Combine(stagingDir, fileEntry.PackagePath);
                if (!Directory.Exists(source) && !File.Exists(source))
                {
                    _log.Report($"Overslaan, ontbreekt in pakket: {fileEntry.DisplayName}");
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
            }

            foreach (var settingsEntry in manifest.Settings)
            {
                ct.ThrowIfCancellationRequested();
                var app = KnownApps.GetAll().FirstOrDefault(a => a.Id == settingsEntry.AppId);

                if (settingsEntry.HasDataFolder)
                {
                    string dataSource = Path.Combine(stagingDir, "settings", settingsEntry.AppId, "data");
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
                    string regFile = Path.Combine(stagingDir, "settings", settingsEntry.AppId, "registry.reg");
                    if (File.Exists(regFile))
                    {
                        _log.Report($"Registerinstellingen terugzetten: {settingsEntry.DisplayName} ...");
                        await ImportRegistryFileAsync(regFile, ct);
                    }
                }
            }

            _log.Report("Terugzetten voltooid.");
        }
        finally
        {
            TryDeleteDirectory(stagingDir);
        }
    }

    private async Task CopyDirectoryAsync(string sourceDir, string destinationDir, bool overwrite, CancellationToken ct)
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
                string dest = filePath.Replace(sourceDir, destinationDir);
                if (!overwrite && File.Exists(dest))
                    continue;
                try { File.Copy(filePath, dest, overwrite: true); }
                catch (IOException) { /* bestand in gebruik - overslaan */ }
                catch (UnauthorizedAccessException) { /* geen toegang - overslaan */ }
            }
        }, ct);
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
