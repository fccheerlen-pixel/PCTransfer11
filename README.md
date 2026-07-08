# PCTransfer11 (C#/.NET, Windows 11)

Een eigen versie van IObit PCtransfer, door **Darkerst Inc.**: zet bestanden,
programma-instellingen én persoonlijke Windows-instellingen over naar een
andere Windows-pc, via het netwerk of via een back-upbestand. Gebouwd met
WPF (.NET 8) in een Windows 11-achtige stijl.

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

### Portable versie

```
dotnet publish PCTransfer11/PCTransfer11.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Het resultaat in `publish/` is één zelfstandige `.exe` — kopieer die map
(of zip 'm) naar een USB-stick en start 'm direct op, zonder installatie.

### Installer (setup.exe)

Naast de portable versie is er ook een échte installer, gebouwd met
[Inno Setup](https://jrsoftware.org/isinfo.php) (gratis):

1. Publiceer eerst de portable versie zoals hierboven (de installer bouwt
   voort op de `publish`-map).
2. Installeer Inno Setup.
3. Open `installer/setup.iss` in Inno Setup en klik op **Compile**, of via
   de command line:
   ```
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
   ```
4. Het resultaat staat in `installer/Output/PCTransfer11-Setup.exe` — een
   normale Windows-installer met Start-menu-snelkoppeling, optionele
   bureaubladsnelkoppeling en een nette "Programma's verwijderen"-ingang.
   De portable versie blijft gewoon apart bruikbaar; de installer is een
   toevoeging, geen vervanging.

### Automatisch bouwen via GitHub Actions

Bij elke push naar `main` bouwt de workflow in `.github/workflows/build.yml`
automatisch **drie** artefacten (zichtbaar onder het tabblad "Actions" van
de run):
- `PCTransfer11-portable` — de portable versie als `.zip`
- `PCTransfer11-windows` — dezelfde portable versie als losse map
- `PCTransfer11-installer` — `PCTransfer11-Setup.exe`

## Wat het doet

- **Tab 1 – Selecteren** (nu in twee kolommen naast elkaar, zodat alles in
  één oogopslag te zien is):
  - **Links – Bestanden en mappen**: Documenten, Afbeeldingen, Bureaublad,
    Video's, Muziek, Downloads - alleen van het eigen gebruikersprofiel,
    niet van het openbare/gedeelde "Public"-profiel - of voeg zelf een map
    toe.
  - **Rechts – Programma- en Windows-instellingen**, gegroepeerd per
    categorie (met "Alles"/"Niets"-knoppen):
    - **Browsers**: Chrome, Edge, Opera, Firefox, Internet Explorer/Edge-
      favorieten.
    - **Communicatie & e-mail**: Thunderbird, Outlook (.pst/.ost), Skype.
    - **Multimedia & downloads**: qBittorrent, AIMP, iTunes.
    - **Ontwikkeltools**: VS Code, Windows Terminal.
    - **Windows-instellingen**: bureaubladachtergrond/kleuren/thema,
      Verkenner- en taakbalk-weergave, muis/toetsenbord, taal- en
      regio-instellingen, geluidsschema — allemaal via je eigen
      `HKEY_CURRENT_USER`-registerdeel, zodat er geen adminrechten nodig
      zijn en er nooit in het systeembrede register wordt geschreven.
    - **Netwerk**:
      - Proxy-instelling (`Instellingen > Netwerk en internet > Proxy`) —
        via het register, geen adminrechten nodig.
      - Gekoppelde netwerkschijven (welke letter naar welk pad wijst) —
        via het register, geen adminrechten nodig. De bijbehorende
        inloggegevens staan in Windows Credential Manager en gaan nooit
        mee; je vult die na het terugzetten eenmalig opnieuw in.
      - **Netwerkadapter** (vast IP-adres, DNS-servers, gateway) en de
        **systeembrede proxy** — vereist adminrechten: Windows toont zowel
        bij het maken als bij het terugzetten van de back-up een
        UAC-bevestigingsvenster. Weiger je die, dan wordt dit onderdeel
        overgeslagen.
      - **Wifi-netwerken** (SSID's + wachtwoorden) — vraagt eveneens om een
        UAC-bevestiging, omdat Windows wachtwoorden alleen in klare tekst
        vrijgeeft aan een administratorproces. Weiger je de UAC-prompt, dan
        valt de app automatisch terug op een versie zonder wachtwoord (dat
        lukt wél zonder adminrechten) - je vult het wachtwoord dan zelf
        eenmalig in op de nieuwe pc.

    Alleen wat op déze pc daadwerkelijk gevonden is, is aan te vinken.
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
- **Tab "Over"**: naam, versie en eigenaar van de software (**Darkerst
  Inc.**).

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
- **"Instellingen" is bewust beperkt** tot een vaste, veilige set: de
  AppData-map van een paar bekende apps, plus een aantal losse
  `HKEY_CURRENT_USER`-registersleutels voor persoonlijke Windows-voorkeuren
  (bureaubladachtergrond, Verkenner/taakbalk, muis/toetsenbord, taal/regio,
  geluidsschema, netwerkschijven, proxy) en de netwerkadapter/Wifi-
  onderdelen hieronder. Er wordt **niet** geprobeerd het hele Windows-
  register over te zetten of alle geïnstalleerde programma's mee te nemen —
  dat is fragiel en kan een systeem juist beschadigen als het tussen
  verschillende Windows-versies/pc's gebeurt.
- **Netwerkadapter en Wifi-wachtwoorden vragen om adminrechten (UAC).**
  Dit zijn de **enige twee** onderdelen van de hele app die dat doen — al
  het andere (inclusief de rest van "Netwerk") werkt bewust zonder. Vink je
  "Netwerkadapter" of "Wifi-netwerken" aan, dan toont Windows bij het
  starten van de back-up/terugzetactie een UAC-bevestigingsvenster (de app
  herlanceert zichzelf daarvoor heel even, onzichtbaar, alleen voor dat ene
  commando - de rest van de app blijft gewoon zonder adminrechten draaien).
  Weiger je die prompt, dan wordt het adapter-onderdeel overgeslagen en valt
  Wifi automatisch terug op een versie zonder wachtwoord.
- **Wachtwoorden gaan verder nooit mee.** Opgeslagen browserwachtwoorden en
  inloggegevens van netwerkschijven (Credential Manager) zijn met DPAPI
  versleuteld aan het Windows-gebruikersaccount van de bronmachine
  gekoppeld en zijn op een andere pc/account toch onbruikbaar — daarom
  neemt dit programma ze bewust niet mee (Wifi-wachtwoorden zijn hierop de
  uitzondering, want die zijn wél gewoon overdraagbaar).
- **Firewall-prompt.** De eerste keer dat je op "Start ontvangen" klikt, kan
  Windows Defender Firewall vragen of de app netwerktoegang mag hebben — klik
  op "Toegang toestaan", anders werkt de netwerkmodus niet.
- **Bestanden die in gebruik zijn** (bv. een geopende browser tijdens het
  kopiëren van diens instellingenmap) worden overgeslagen in plaats van de
  hele overdracht te laten mislukken — sluit browsers/apps dus liever eerst
  af voor een volledige overdracht.
- Draait **standaard zonder** adminrechten (`asInvoker` in het manifest) —
  op de twee hierboven genoemde uitzonderingen na blijven alle bewerkingen
  binnen de gebruikersmappen en `HKEY_CURRENT_USER`.

## Projectstructuur

```
PCTransfer11.sln
installer/
  setup.iss                    - Inno Setup-script voor de setup.exe
.github/workflows/build.yml    - bouwt portable zip + installer op elke push
PCTransfer11/
  PCTransfer11.csproj
  app.manifest
  App.xaml(.cs)
  MainWindow.xaml(.cs)
  Models/
    FileSelectionItem.cs      - een aan te vinken map/bestand
    AppProfile.cs              - een bekende applicatie of Windows-instelling
    PackageManifest.cs         - manifest.json-structuur in elk pakket
    DiscoveredReceiver.cs      - via UDP gevonden ontvanger
    RestoreSelectionItem.cs     - aan te vinken item op het terugzet-scherm
  Services/
    KnownApps.cs                - de vooraf gedefinieerde app-/instellingenlijst, per categorie
    PackageBuilder.cs           - bouwt een back-up (map, of tijdelijk gezipt voor het netwerk)
    PackageRestorer.cs          - leest een back-up in en zet (een selectie) terug
    NetworkReceiver.cs          - TCP-ontvangst + UDP-discovery-antwoord
    NetworkSender.cs            - UDP-discovery + TCP-verzending
    NetworkSettingsExporter.cs  - Wifi-profielen exporteren/importeren via netsh (zonder adminrechten)
    ElevatedNetworkHelper.cs    - vraagt eenmalig adminrechten (UAC) voor netwerkadapter/Wifi-wachtwoorden
```

## Uitbreiden

Wil je een extra applicatie of Windows-instelling toevoegen aan de
instellingenlijst? Voeg een nieuw `AppProfile`-object toe in
`Services/KnownApps.cs` met een `Category` (voor de groepskop in de UI) en
óf een `ResolveDataFolder`, óf één of meer `RegistryKeys`
(`HKEY_CURRENT_USER`-sleutels), óf een `CustomExport`/`CustomImport`-paar
voor iets bijzonders (zoals de Wifi-export) — je hoeft verder nergens iets
aan te passen, de UI en het pakketformaat pakken het automatisch op.
