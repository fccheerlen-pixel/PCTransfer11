# PCTransfer11 (C#/.NET, Windows 11)

Een eigen versie van IObit PCtransfer: zet bestanden en een selectie
programma-instellingen over naar een andere Windows-pc, via het netwerk
of via een back-upbestand. Gebouwd met WPF (.NET 8) in een Windows
11-achtige stijl.

**Ook dit project is hier niet gecompileerd of getest** — deze omgeving heeft
geen .NET SDK en geen Windows/internettoegang. De code volgt standaard,
moderne .NET/WPF-patronen, maar jij moet 'm bouwen op Windows en eventuele
kleine build-foutjes fixen (plak de foutmelding gewoon terug, dan help ik
verder — dat hebben we bij de Delphi/Lazarus-versie ook zo gedaan).

## Bouwen

1. Installeer de **.NET 8 SDK** (gratis): https://dotnet.microsoft.com/download/dotnet/8.0
   — kies "SDK", niet alleen "Runtime".
2. Open `PCTransfer11.sln` in **Visual Studio 2022** (gratis Community-editie
   volstaat, zorg dat de workload ".NET desktop development" is aangevinkt),
   óf bouw via de command line:
   ```
   cd PCTransfer11
   dotnet build
   dotnet run
   ```
3. Er zijn **geen externe NuGet-packages** nodig — alles gebruikt de
   ingebouwde .NET/WPF-bibliotheken, dus `dotnet build` werkt zonder
   afhankelijkheden op te halen (op de .NET SDK zelf na).

## Wat het doet

- **Tab 1 – Selecteren**: vink mappen aan (Documenten, Afbeeldingen,
  Bureaublad, Video's, Muziek, Downloads - elk zowel de eigen als de
  openbare/gedeelde "Public"-variant - of voeg zelf een map toe) en/of
  instellingen/gegevens van bekende apps: Chrome, Edge, Opera, Firefox,
  Internet Explorer/Edge-favorieten, Thunderbird, Outlook (.pst/.ost),
  Skype, VS Code, Windows Terminal, qBittorrent, AIMP, iTunes en de
  bureaubladachtergrond. Alleen wat op déze pc daadwerkelijk gevonden is,
  is aan te vinken.
- **Tab 2 – Overzetten**, twee modi:
  - **Netwerk**: de ontvangende pc klikt op "Start ontvangen" en luistert;
    de zendende pc klikt op "Zoek pc's op het netwerk" (automatische
    detectie via UDP-broadcast) of vult handmatig een IP-adres in, en klikt
    op "Start verzenden". Werkt alleen als beide pc's op hetzelfde
    (Wifi-)netwerk zitten. Achter de schermen wordt de back-up hiervoor
    tijdelijk ingepakt tot één zip-bestand voor de overdracht.
  - **Back-upmap**: maakt een **gewone map** (geen zip) met een submap per
    geselecteerd onderdeel (bv. `Documenten`, `Afbeeldingen`) plus een
    `manifest.json` en een `_instellingen`-map. Omdat het gewone mappen en
    bestanden zijn, kan je de back-up direct in Verkenner openen, bekijken
    en zelfs bewerken voordat je 'm terugzet. Zet de map op een USB-stick of
    externe schijf om naar de nieuwe pc over te brengen.
  - **Terugzetten**: kies op de nieuwe pc de back-upmap via "Back-upmap
    kiezen ...". De app leest het manifest in en toont een aanvinklijst van
    alles wat er in de back-up zit (elke map en elke app-instelling apart),
    zodat je bijvoorbeeld alleen "Afbeeldingen" of alleen "Documenten" kan
    terugzetten in plaats van alles.
- **Tab 3 – Voortgang**: voortgangsbalk met percentage, een **Stop-knop**
  (annuleert de lopende back-up/overdracht/terugzetactie op een moment dat
  veilig is - wat al gekopieerd was blijft gewoon staan) en een logboek.

## Nieuw: voortgang, annuleren, cloudbestanden en crashrapport

- **Echt percentage in plaats van "bezig"**: vóórdat er iets gekopieerd
  wordt, telt de app eerst de totale hoeveelheid data (pre-scan), zodat de
  voortgangsbalk en het percentage kloppen in plaats van alleen per-item te
  springen.
- **Stop-knop**: elke lange actie (back-up maken, terugzetten, netwerk
  verzenden/ontvangen) is nu annuleerbaar. Kopiëren gebeurt in blokken van
  1 MB met een cancellation-check per blok, dus de Stop-knop reageert ook
  meteen tijdens het kopiëren van een groot bestand (bv. een video van
  enkele GB's) in plaats van pas nadat dat ene bestand klaar is.
- **Cloud-only bestanden (OneDrive e.d.) worden gedetecteerd en
  overgeslagen** in plaats van geprobeerd te downloaden. Dit was de meest
  waarschijnlijke oorzaak van het "soms vastlopen" tijdens een back-up: een
  bestand dat alleen online staat, forceert bij het lezen een download die
  bij een grote video of trage verbinding minutenlang kan duren zonder dat
  er iets in de UI gebeurt. De app meldt in het logboek hoeveel van
  dit soort bestanden zijn overgeslagen.
- **Crashrapport**: als de app toch onverwacht crasht (bv. tijdens het
  opstarten, of ergens anders), verschijnt er nu een venster met de
  volledige foutmelding + stack trace in een selecteerbaar tekstvak, plus
  een "Kopiëren"-knop. Het rapport wordt ook automatisch weggeschreven naar
  `%LOCALAPPDATA%\PCTransfer11\crashes\`, zodat er sowieso een bestand is
  om door te sturen, ook als het venster per ongeluk wordt weggeklikt.
- **Eigen pictogram** voor de gebouwde `.exe` (was voorheen het standaard
  .NET-icoontje).

## Belangrijke kanttekeningen

- **Geen versleuteling op het netwerk.** De netwerkoverdracht gebruikt platte
  TCP, zonder encryptie. Prima voor een vertrouwd thuisnetwerk, **niet**
  geschikt om over het open internet of een onvertrouwd (bv. openbaar Wifi)
  netwerk te sturen.
- **Wachtwoorden gaan nooit mee.** Opgeslagen browserwachtwoorden zijn met
  DPAPI versleuteld aan het Windows-gebruikersaccount van de bronmachine
  gekoppeld en zijn op een andere pc/account toch onbruikbaar — daarom neemt
  dit programma ze bewust niet mee.
- **"Instellingen" is bewust beperkt** tot een kleine, veilige set: de
  AppData-map van een paar bekende apps, plus (optioneel) één
  `HKEY_CURRENT_USER`-registersleutel voor de bureaubladachtergrond. Er wordt
  **niet** geprobeerd het hele Windows-register of alle geïnstalleerde
  programma's over te zetten — dat is fragiel en kan een systeem juist
  beschadigen als het tussen verschillende Windows-versies/pc's gebeurt.
- **Firewall-prompt.** De eerste keer dat je op "Start ontvangen" klikt, kan
  Windows Defender Firewall vragen of de app netwerktoegang mag hebben — klik
  op "Toegang toestaan", anders werkt de netwerkmodus niet.
- **Bestanden die in gebruik zijn** (bv. een geopende browser tijdens het
  kopiëren van diens instellingenmap) worden overgeslagen in plaats van de
  hele overdracht te laten mislukken — sluit browsers/apps dus liever eerst
  af voor een volledige overdracht.
- Draait bewust **zonder** adminrechten (`asInvoker` in het manifest) — alle
  bewerkingen blijven binnen de gebruikersmappen en `HKEY_CURRENT_USER`.

## Projectstructuur

```
PCTransfer11.sln
PCTransfer11/
  PCTransfer11.csproj
  app.manifest
  App.xaml(.cs)
  MainWindow.xaml(.cs)
  Models/
    FileSelectionItem.cs      - een aan te vinken map/bestand
    AppProfile.cs              - een bekende applicatie (Chrome, VS Code, ...)
    PackageManifest.cs         - manifest.json-structuur in elk pakket
    DiscoveredReceiver.cs      - via UDP gevonden ontvanger
    RestoreSelectionItem.cs     - aan te vinken item op het terugzet-scherm
  Services/
    KnownApps.cs                - de vooraf gedefinieerde app-lijst
    PackageBuilder.cs           - bouwt een back-up (map, of tijdelijk gezipt voor het netwerk)
    PackageRestorer.cs          - leest een back-up in en zet (een selectie) terug
    NetworkReceiver.cs          - TCP-ontvangst + UDP-discovery-antwoord
    NetworkSender.cs            - UDP-discovery + TCP-verzending
```

## Uitbreiden

Wil je een extra applicatie toevoegen aan de instellingen-lijst? Voeg een
nieuw `AppProfile`-object toe in `Services/KnownApps.cs` — je hoeft verder
nergens iets aan te passen, de UI en het pakketformaat pakken het automatisch
op.
