using System.IO;

namespace PCTransfer11.Models;

/// <summary>
/// Eén door de gebruiker aan te vinken bestands- of map-item, met een
/// vriendelijke naam voor in de lijst.
/// </summary>
public sealed class FileSelectionItem
{
    public string DisplayName { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsChecked { get; set; }
    public bool Exists => Directory.Exists(Path) || File.Exists(Path);

    public static FileSelectionItem ForSpecialFolder(string displayName, Environment.SpecialFolder folder)
    {
        return new FileSelectionItem
        {
            DisplayName = displayName,
            Path = Environment.GetFolderPath(folder)
        };
    }
}
