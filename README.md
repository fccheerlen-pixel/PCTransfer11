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
  Bureaublad, Video's, Muziek, Downloads, of voeg zelf een map toe) en/of
  instellingen van bekende apps (Chrome, Edge, Firefox, VS Code, Windows
  Terminal, bureaubladachtergrond). Alleen wat op déze pc daadwerkelijk
  gevonden is, is aan te vinken.
- **Tab 2 – Overzetten**, twee modi:
  - **Netwerk**: de ontvangende pc klikt op "Start ontvangen" en luistert;
    de zendende pc klikt op "Zoek pc's op het netwerk" (automatische
    detectie via UDP-broadcast) of vult handmatig een IP-adres in, en klikt
    op "Start verzenden". Werkt alleen als beide pc's op hetzelfde
    (Wifi-)netwerk zitten.
  - **Back-upbestand**: maakt één `.pctbackup`-bestand (een zip met een
    manifest) dat je op een USB-stick/externe schijf zet en op de nieuwe pc
    weer inleest.
- **Tab 3 – Voortgang**: voortgangsbalk en logboek.

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
  Services/
    KnownApps.cs                - de vooraf gedefinieerde app-lijst
    PackageBuilder.cs           - bouwt een .pctbackup-pakket (zip)
    PackageRestorer.cs          - pakt een pakket uit en zet het terug
    NetworkReceiver.cs          - TCP-ontvangst + UDP-discovery-antwoord
    NetworkSender.cs            - UDP-discovery + TCP-verzending
```

## Uitbreiden

Wil je een extra applicatie toevoegen aan de instellingen-lijst? Voeg een
nieuw `AppProfile`-object toe in `Services/KnownApps.cs` — je hoeft verder
nergens iets aan te passen, de UI en het pakketformaat pakken het automatisch
op.
