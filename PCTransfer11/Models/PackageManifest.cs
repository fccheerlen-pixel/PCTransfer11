namespace PCTransfer11.Models;

/// <summary>
/// Wordt als manifest.json in elk PCTransfer11-pakket opgeslagen, zodat het
/// pakket bij het terugzetten weet welke bestanden waar terug moeten en
/// welke instellingen bij welke applicatie horen.
/// </summary>
public sealed class PackageManifest
{
    public string CreatedByMachine { get; set; } = Environment.MachineName;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string ToolVersion { get; set; } = "1.0.0";

    public List<FileEntry> Files { get; set; } = new();
    public List<SettingsEntry> Settings { get; set; } = new();

    public sealed class FileEntry
    {
        /// <summary>Relatieve mapnaam van dit item binnen de back-up (bv. "Documenten").</summary>
        public string PackagePath { get; set; } = "";
        /// <summary>Absoluut oorspronkelijk pad op de bronmachine (voor herstel op dezelfde gebruikersmap-structuur).</summary>
        public string OriginalPath { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public sealed class SettingsEntry
    {
        public string AppId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        /// <summary>Relatief pad binnen "settings/{AppId}/data", of null als er geen datamap is.</summary>
        public bool HasDataFolder { get; set; }
        /// <summary>Of er een registry.reg-bestand is meegepakt.</summary>
        public bool HasRegistryExport { get; set; }
        public string? RegistryKey { get; set; }
    }
}
