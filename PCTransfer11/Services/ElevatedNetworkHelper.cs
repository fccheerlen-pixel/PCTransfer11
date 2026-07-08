using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PCTransfer11.Services;

/// <summary>
/// Sommige netwerkinstellingen (vast IP/DNS/gateway per adapter, de
/// systeembrede proxy, en Wifi-wachtwoorden in klare tekst) staan in
/// HKEY_LOCAL_MACHINE of zijn alleen met adminrechten op te vragen. De rest
/// van PCTransfer11 draait bewust zonder adminrechten (asInvoker), dus voor
/// alleen déze onderdelen herlanceert de app zichzelf kort, onzichtbaar en
/// met een UAC-verzoek (via "runas") om precies dat ene commando uit te
/// voeren. De hoofd-app wacht daarna gewoon op het resultaat.
///
/// Herkent zichzelf via de command-line-argumenten "--elevated-export" /
/// "--elevated-import" - zie <see cref="TryHandleElevatedArgs"/>, die
/// helemaal vooraan in App.xaml.cs wordt aangeroepen.
/// </summary>
public static class ElevatedNetworkHelper
{
    private const string ExportFlag = "--elevated-export";
    private const string ImportFlag = "--elevated-import";

    // ---------------------------------------------------------------
    // Aanroepen vanuit de (niet-elevated) hoofd-app
    // ---------------------------------------------------------------

    /// <summary>
    /// Herlanceert deze .exe met adminrechten (UAC-prompt) om de gevraagde
    /// export uit te voeren. Weigert de gebruiker de UAC-prompt, dan wordt
    /// dat netjes gemeld en - alleen voor Wifi - alsnog een back-up zonder
    /// wachtwoord geprobeerd (dat lukt namelijk wel zonder adminrechten).
    /// </summary>
    public static async Task<bool> RunElevatedExportAsync(string kind, string destDir, CancellationToken ct, IProgress<string> log)
    {
        Directory.CreateDirectory(destDir);
        log.Report("Windows vraagt nu om adminrechten (UAC) om deze netwerkinstelling op te halen ...");

        bool ok = await LaunchElevatedSelfAsync($"{ExportFlag} {kind} \"{destDir}\"", ct, log);

        if (!ok && kind == "wifi")
        {
            log.Report("Geen adminrechten gekregen - Wifi-netwerken worden zonder wachtwoord opgehaald " +
                       "(dat lukt wel zonder UAC) ...");
            ok = await NetworkSettingsExporter.ExportWifiProfilesAsync(destDir, ct, log);
        }
        else if (!ok)
        {
            log.Report("Geen adminrechten gekregen - dit onderdeel wordt overgeslagen.");
        }

        return ok;
    }

    /// <summary>Zelfde principe als <see cref="RunElevatedExportAsync"/>, maar dan voor terugzetten.</summary>
    public static async Task RunElevatedImportAsync(string kind, string sourceDir, CancellationToken ct, IProgress<string> log)
    {
        if (!Directory.Exists(sourceDir)) return;

        log.Report("Windows vraagt nu om adminrechten (UAC) om deze netwerkinstelling terug te zetten ...");
        bool ok = await LaunchElevatedSelfAsync($"{ImportFlag} {kind} \"{sourceDir}\"", ct, log);

        if (!ok && kind == "wifi")
        {
            log.Report("Geen adminrechten gekregen - Wifi-profielen zonder wachtwoord worden alsnog toegevoegd " +
                       "(je vult het wachtwoord dan zelf eenmalig in) ...");
            await NetworkSettingsExporter.ImportWifiProfilesAsync(sourceDir, ct, log);
        }
        else if (!ok)
        {
            log.Report("Geen adminrechten gekregen - dit onderdeel is niet teruggezet.");
        }
    }

    private static async Task<bool> LaunchElevatedSelfAsync(string arguments, CancellationToken ct, IProgress<string> log)
    {
        Process? process = null;
        try
        {
            string exePath = Environment.ProcessPath
                              ?? Process.GetCurrentProcess().MainModule?.FileName
                              ?? throw new InvalidOperationException("Kan het pad naar PCTransfer11.exe niet bepalen.");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = true,   // vereist voor "runas"
                Verb = "runas",           // vraagt om de UAC-prompt
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            try { process?.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: gebruiker weigerde de UAC-prompt
        {
            log.Report("UAC-prompt geannuleerd door de gebruiker.");
            return false;
        }
        catch (Exception ex)
        {
            log.Report($"Kon niet als administrator uitvoeren: {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------
    // De elevated (onzichtbare, kortstondige) helper-modus zelf
    // ---------------------------------------------------------------

    /// <summary>
    /// Wordt helemaal vooraan in App.OnStartup aangeroepen. Geeft true terug
    /// als de opgegeven command-line-argumenten een elevated-helperverzoek
    /// waren (in dat geval is de actie al uitgevoerd en moet de app direct
    /// afsluiten zonder een venster te tonen).
    /// </summary>
    public static bool TryHandleElevatedArgs(string[] args)
    {
        if (args.Length != 3) return false;

        string flag = args[0];
        string kind = args[1];
        string dir = args[2];

        if (flag == ExportFlag)
        {
            RunExportWorker(kind, dir);
            return true;
        }
        if (flag == ImportFlag)
        {
            RunImportWorker(kind, dir);
            return true;
        }
        return false;
    }

    private static void RunExportWorker(string kind, string destDir)
    {
        try
        {
            Directory.CreateDirectory(destDir);
            switch (kind)
            {
                case "adapter":
                    RunNetshRedirectToFile("interface dump", Path.Combine(destDir, "netcfg.txt"));
                    RunNetshRedirectToFile("winhttp show proxy", Path.Combine(destDir, "proxy_systeem.txt"));
                    break;
                case "wifi":
                    RunNetsh($"wlan export profile folder=\"{destDir}\" key=clear");
                    break;
            }
        }
        catch { /* best effort - een lege/ontbrekende map betekent gewoon "mislukt" voor de aanroeper */ }
    }

    private static void RunImportWorker(string kind, string sourceDir)
    {
        try
        {
            switch (kind)
            {
                case "adapter":
                    string netcfg = Path.Combine(sourceDir, "netcfg.txt");
                    if (File.Exists(netcfg))
                        RunNetsh($"-f \"{netcfg}\"");
                    break;
                case "wifi":
                    if (!Directory.Exists(sourceDir)) return;
                    foreach (string xml in Directory.GetFiles(sourceDir, "*.xml"))
                        RunNetsh($"wlan add profile filename=\"{xml}\" user=all");
                    break;
            }
        }
        catch { /* best effort */ }
    }

    private static void RunNetsh(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi);
        process?.WaitForExit();
    }

    private static void RunNetshRedirectToFile(string arguments, string outputFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi);
        if (process == null) return;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        File.WriteAllText(outputFile, output);
    }
}
