namespace PCTransfer11.Models;

/// <summary>
/// Beschrijft waar de instellingen/gegevens van een bekende applicatie
/// op schijf (en eventueel in het register) staan, zodat PCTransfer11
/// ze kan meenemen in een pakket.
/// </summary>
public sealed class AppProfile
{
    /// <summary>Unieke, korte sleutel - wordt gebruikt als mapnaam in het pakket.</summary>
    public required string Id { get; init; }

    /// <summary>Naam zoals getoond in de UI.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Of dit item standaard aangevinkt staat.</summary>
    public bool IsChecked { get; set; }

    /// <summary>
    /// Levert het absolute pad naar de map met gegevens op, of null als de
    /// applicatie niet op dit systeem is geïnstalleerd / gevonden kan worden.
    /// </summary>
    public required Func<string?> ResolveDataFolder { get; init; }

    /// <summary>
    /// Optioneel: een HKEY_CURRENT_USER-registersleutel (volledig pad, bv.
    /// "HKEY_CURRENT_USER\Software\Microsoft\Notepad") die wordt
    /// geëxporteerd/geïmporteerd via het ingebouwde reg.exe.
    /// </summary>
    public string? RegistryKey { get; init; }

    /// <summary>Korte toelichting, getoond als tooltip.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// Of deze applicatie/instelling op dit systeem gevonden is en dus
    /// aangevinkt kan worden. Wordt door de UI gebruikt om niet-gevonden
    /// items uit te grijzen.
    /// </summary>
    public bool IsAvailable => ResolveDataFolder() != null || RegistryKey != null;
}
