using System.IO;
using PCTransfer11.Models;

namespace PCTransfer11.Services;

/// <summary>
/// Bevat de vooraf gedefinieerde lijst van applicaties waarvan PCTransfer11
/// instellingen/gegevens kan meenemen. Makkelijk uit te breiden: voeg een
/// nieuw AppProfile toe aan de lijst in <see cref="GetAll"/>.
/// </summary>
public static class KnownApps
{
    public static List<AppProfile> GetAll()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return new List<AppProfile>
        {
            new AppProfile
            {
                Id = "chrome",
                DisplayName = "Google Chrome (bladwijzers, instellingen)",
                ResolveDataFolder = () =>
                {
                    var path = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default");
                    return Directory.Exists(path) ? path : null;
                },
                Note = "Bladwijzers en voorkeuren. Opgeslagen wachtwoorden gaan NIET mee " +
                       "(die zijn versleuteld aan dit Windows-account gekoppeld)."
            },
            new AppProfile
            {
                Id = "edge",
                DisplayName = "Microsoft Edge (bladwijzers, instellingen)",
                ResolveDataFolder = () =>
                {
                    var path = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default");
                    return Directory.Exists(path) ? path : null;
                },
                Note = "Bladwijzers en voorkeuren. Opgeslagen wachtwoorden gaan NIET mee."
            },
            new AppProfile
            {
                Id = "firefox",
                DisplayName = "Mozilla Firefox (bladwijzers, instellingen)",
                ResolveDataFolder = () =>
                {
                    var profilesRoot = Path.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles");
                    if (!Directory.Exists(profilesRoot)) return null;
                    // Pak het eerste profiel dat op ".default-release" eindigt, anders het eerste dat er is.
                    var dirs = Directory.GetDirectories(profilesRoot);
                    var preferred = dirs.FirstOrDefault(d => d.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase));
                    return preferred ?? dirs.FirstOrDefault();
                },
                Note = "Bladwijzers, geschiedenis en voorkeuren van het standaardprofiel."
            },
            new AppProfile
            {
                Id = "vscode",
                DisplayName = "Visual Studio Code (instellingen, sneltoetsen)",
                ResolveDataFolder = () =>
                {
                    var path = Path.Combine(roamingAppData, "Code", "User");
                    return Directory.Exists(path) ? path : null;
                },
                Note = "settings.json, keybindings.json en snippets. Extensies zelf gaan niet mee."
            },
            new AppProfile
            {
                Id = "winterminal",
                DisplayName = "Windows Terminal (instellingen)",
                ResolveDataFolder = () =>
                {
                    var packagesRoot = Path.Combine(localAppData, "Packages");
                    if (!Directory.Exists(packagesRoot)) return null;
                    var dir = Directory.GetDirectories(packagesRoot, "Microsoft.WindowsTerminal_*").FirstOrDefault();
                    if (dir == null) return null;
                    var settingsDir = Path.Combine(dir, "LocalState");
                    return Directory.Exists(settingsDir) ? settingsDir : null;
                },
                Note = "settings.json met kleurenschema's, profielen en sneltoetsen."
            },
            new AppProfile
            {
                Id = "desktop",
                DisplayName = "Bureaubladachtergrond en persoonlijke instellingen",
                ResolveDataFolder = () => null, // alleen registry, geen bestandsmap
                RegistryKey = @"HKEY_CURRENT_USER\Control Panel\Desktop",
                Note = "Achtergrondafbeelding-instelling en gerelateerde voorkeuren (via het register)."
            },
        };
    }
}
