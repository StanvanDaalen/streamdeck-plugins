# Stream Deck-plugins

Eigen plugins voor de Elgato Stream Deck op Windows. De F1-plugin werkt ook op Linux met OpenDeck.

## Wat zit erin

| Plugin | Bestand | Waarvoor |
|---|---|---|
| [Volume & Launcher](#volume--launcher) | `VolumeLauncher.streamDeckPlugin` | Volume-dials voor games en apps, en knoppen die games en apps starten, verbergen en sluiten |
| [Scarlett Switch](#scarlett-switch) | `ScarlettSwitch.streamDeckPlugin` | Wisselen tussen speakers en koptelefoon op een Focusrite Scarlett |
| [Monitor](#monitor) | `Monitor.streamDeckPlugin` | Wisselen tussen de inputs van een monitor |
| [Better Spotify Dial](#better-spotify-dial) | `BetterSpotifyDial.streamDeckPlugin` | Spotify op een dial, met eigen acties per klik |
| [PlayStation 5 for OBS](#playstation-5-for-obs) | `PlayStation5-OBS.streamDeckPlugin` | Volume van een PS5 die je via OBS en een capturekaart hoort |
| [F1](#f1) | `F1.streamDeckPlugin` | F1 kijken met MultiViewer, ook op Linux |

Wat er per versie nieuw of verbeterd is, staat in [CHANGELOG.md](CHANGELOG.md) en bij elke [release](https://github.com/StanvanDaalen/streamdeck-plugins/releases).

## Installeren

1. Open de [nieuwste release](https://github.com/StanvanDaalen/streamdeck-plugins/releases/latest).
2. Download de plugins die je wilt (zie de tabel hierboven).
3. Dubbelklik op het bestand en bevestig de installatie in Stream Deck.

Op Linux (alleen F1): installeer [OpenDeck](https://github.com/nekename/OpenDeck), open daarin het tabblad **Plugins** en kies het gedownloade `F1.streamDeckPlugin`.

.NET hoeft niet geïnstalleerd te zijn. De plugins zijn niet gesigneerd, dus Windows of Stream Deck kan een waarschuwing tonen.

**Van een oudere versie?** Audio en Launcher zijn samen nu **Volume & Launcher**, en de Scarlett-knoppen zitten in de losse plugin **Scarlett Switch**. Installeer Volume & Launcher over Audio heen: je Game Volume- en App Volume-dials blijven gewoon werken. Verwijder daarna de oude Launcher-plugin in Stream Deck en zet je Game- en App-knoppen opnieuw neer uit de categorie Volume & Launcher. Gebruik je de Scarlett-knoppen, installeer dan ook Scarlett Switch en zet die knoppen opnieuw neer.

## Volume & Launcher

Volume-dials voor de game die je speelt en voor elke app, en knoppen om games en apps te starten, te verbergen en te sluiten. Alles deelt één gamebibliotheek: een eigen naam of logo voor een game zie je meteen op alle dials en knoppen van die game.

Games worden automatisch gevonden via Steam, Epic, Xbox en Lunar Client. Andere games voeg je zelf toe in de Gamebibliotheek.

### Game Volume (dial)

Regelt het volume van de game die nu open staat, met de naam en het logo van die game. Kies je onder **Game** één vaste game, dan hoort de dial altijd bij die game; staat die dicht, dan toont de dial hem grijs.

- **Draaien**: volume, in stappen van 1, 2, 3, 5 of 10% (**Volume step**).
- **Klik, dubbelklik, driemaal klikken en vasthouden** (ook tikken en lang tikken op het scherm): per gebaar te kiezen uit dempen, minimaliseren/openen, sluiten, favoriet volume of niets. Standaard: klik = dempen, dubbelklik = minimaliseren/openen, vasthouden = sluiten.
- **Ingedrukt draaien**: met **Game/Discord balance** aan schuif je het volume tussen game en Discord: de game harder en Discord zachter, of andersom.
- **Weergave**: als titel de gamenaam of een eigen tekst; het logo klein als icoon of groot als achtergrond; de volumebalk wit of in de kleur van de game.
- **Favoriet volume**: stel per game een vast volume in, of neem het huidige over. Met **Start games at favorite volume** begint de game altijd op dat volume.
- **Game options** (gelden voor alle Game Volume-dials):
  - **Remember volume per game**: elke game start op het volume van de vorige keer.
  - **Game/Discord balance**: zie hierboven.
  - **Lower Spotify while gaming**: Spotify gaat zachter (naar 10 tot 80%) zolang een game open staat, en daarna weer terug.
- **Gamebibliotheek**: kies een game en geef hem een eigen naam of afbeelding, markeer iets als **Not a game** zodat het niet meer als game telt, of verwijder een zelf toegevoegde game. Onder **Add game** voeg je een game toe met een naam en zijn .exe. De game die nu speelt staat bovenaan en is al gekozen.

### App Volume (dial)

Regelt het volume van één app, zoals Discord, Chrome of Spotify.

- **Draaien**: volume. Speelt de app iets af, dan spoelt **ingedrukt draaien** 5 seconden per stap.
- **Klik, dubbelklik, driemaal klikken en vasthouden**: dezelfde keuzes als bij Game Volume, plus afspelen/pauze, volgend nummer en vorig nummer. Standaard: klik = dempen, dubbelklik = minimaliseren/openen, driemaal klikken = vorig nummer, vasthouden = sluiten.
- **Tijdens afspelen** (**While playing**): speelt de app iets af, bijvoorbeeld een YouTube-video, dan toont de dial titel, kanaal, plaatje, afspelen/pauze en een tijdlijn. Stijlen: **Player**, **Picture** (het plaatje over het hele scherm), **Compact** of **Off** (gewone weergave). Kort draaien toont even het volume.
- **App-kenmerk**: zodat je de dial naast de Spotify-dial herkent, krijgt een browser YouTube-rood en Spotify groen. Aan te vinken: op de play-knop, op de tijdlijn en/of als klein zwart-wit logo.
- **Als de app niets afspeelt**: **Player** toont de app in dezelfde stijl met het volume; **Classic** is de oude weergave met icoon, titel en volumebalk (dan zijn ook het logo als achtergrond en de balkkleur te kiezen). Het volume is al in te stellen voordat de app geluid maakt en geldt zodra hij dat doet.
- **App dicht**: de dial toont "Open" met de naam van de app, en een klik start hem.
- **Titel**: de naam van de app of een eigen tekst.

### Game Key (knop)

Een knop met het logo, de naam en de speeltijd van een game.

- Zonder gekozen game volgt de knop **de game die nu open staat**. Kies je onder **Game** één game, dan hoort de knop vast bij die game en kan hij hem ook **starten** (via Steam, Epic, Lunar Client of de .exe).
- **Klik**: starten, minimaliseren of weer tonen. **1 seconde vasthouden**: de game sluiten.
- **Weergave**: de naam tonen terwijl je speelt, de speeltijd tonen terwijl je speelt, en bij een vaste game de naam tonen als hij dicht is.
- **Logo**: kies een eigen afbeelding (PNG, JPG of SVG). Bij een knop die de open game volgt, geldt dat logo voor de game die nu open staat. De plugin bewaart een kopie in `%APPDATA%\StanGameVolume\logos`, dus het logo blijft staan als je het bestand verplaatst. Het logo geldt voor alle knoppen en dials van die game.

### App Key (knop)

Een knop met het icoon en de naam van één app.

- **Klik**: minimaliseren, weer tonen, of openen als de app dicht is.
- **1 seconde vasthouden**: de app sluiten. Blijft hij in het systeemvak draaien, zoals Discord, dan wordt hij na 3 seconden helemaal afgesloten, behalve als er nog een venster open staat (bijvoorbeeld een vraag om op te slaan).
- **Weergave**: met of zonder naam.

## Scarlett Switch

Wisselt een Focusrite Scarlett tussen speakers en koptelefoon, met een knop of met een dial van de Stream Deck +. Nodig: een Focusrite Scarlett, Focusrite Control 2 en een MIDI-loopbackpoort (bijvoorbeeld via MIDI Loopback Setup van Windows). De plugin stuurt het wisselcommando via die poort naar Focusrite Control 2.

- **Scarlett Switch** (knop of dial): wisselt tussen speakers en koptelefoon en toont welke aan staat. Op een dial: drukken of tikken = wisselen, draaien = Windows-volume, lang tikken = dempen.
- **Speakers** en **Headphones** (knoppen): zetten de Scarlett direct op dat apparaat en lichten op zolang het aan staat.

Opties:

- **Status**: of de Scarlett, Focusrite Control 2 en de MIDI-poort gevonden zijn. Rood betekent dat er iets ontbreekt, met de reden erbij.
- **Look** (dial): **Player**, in de stijl van de media- en Spotify-dials met de apparaatnaam als titel, of **Classic** met icoon, titel en volumebalk. Bij Classic kies je als titel het apparaat of een eigen tekst.
- **Speakers** / **Headphones** (dial): eigen namen, zoals "Yamaha HS5" of "DT 1990 Pro".
- **Custom icons** (Scarlett Switch): een eigen icoon voor speakers en voor koptelefoon, uit een bestand of uit de iconen die in Stream Deck geïnstalleerd zijn (**Library**).
- **Speaker mix** / **Headphone mix**: welke mix (A, B of C) van Focusrite Control 2 bij elk apparaat hoort.
- **Focusrite**: Focusrite Control 2 verborgen starten en daarna sluiten, verborgen starten en laten draaien, of nooit zelf starten.
- **Volume step** (dial): 1, 2, 3, 5 of 10% per stap.
- **MIDI**: de naam van de loopbackpoort, en **Apply again** om de instellingen opnieuw naar Focusrite Control 2 te sturen.

Instellingen staan in `%APPDATA%\ScarlettSwitch`.

## Monitor

- **Monitor Input**: wissel met een knop tussen de inputs van een monitor. De monitor moet DDC/CI ondersteunen. Klik = volgende input; vorige input op dubbelklik, lang drukken of uit (**Previous input**).

Vink in het menu alleen inputs aan waar iets op is aangesloten (**Inputs in use**). Een monitor geeft niet door welke inputs signaal hebben.

Opties:

- **Monitor**: welke monitor de knop bedient, met een eigen naam.
- **Edit input**: per input een eigen naam, het apparaat dat erop zit (automatisch, pc, PlayStation 5 Pro of geen) en een eigen icoon uit een bestand of de Bibliotheek.
- **Style**: **Basic**, **Tile** (fel zolang het scherm verbonden is) of **Icon only**, en of er tekst op de knop staat.
- **PlayStation**: houd de pc en de andere schermen wakker zolang de PS5 in beeld is, zodat OBS en Discord blijven draaien, en kies welk scherm dan het hoofdscherm wordt.
- **Disconnect while not on this PC**: zie hieronder.

LG-monitors negeren het standaardcommando voor inputs. Op een pc met een NVIDIA-videokaart stuurt de plugin daarom LG's eigen commando via de videokaart. Een LG-monitor meldt niet welke input actief is; de knop toont de input waar hij zelf het laatst naartoe wisselde. Andere merken, zoals Alienware, werken zoals altijd. De modelnaam komt uit de EDID van de monitor, omdat sommige monitors (waaronder LG) via DDC/CI een verkeerde modelnaam doorgeven.

Optie "Loskoppelen als het niet op deze pc staat" (standaard uit, per knop): wisselt de monitor naar een andere input dan die van deze pc, zoals een console, dan haalt Windows hem uit de schermindeling, zodat de muis direct naar het volgende scherm gaat. Terug naar de pc zet de indeling terug. Lukt dat ooit niet, dan zet Windows + P → Uitbreiden alles terug.

Per input kun je een eigen icoon kiezen. De plugin levert een PC- en een PS5-icoon mee; die staan in de Bibliotheek onder "Monitor".

## Better Spotify Dial

Spotify op een dial van de Stream Deck +, met hetzelfde scherm als de officiële Spotify-plugin: hoes, titel, artiest, tijd, voortgang, shuffle, afspelen en herhalen. Het verschil: je kiest zelf wat de dial doet.

- **1x, 2x en 3x klikken, vasthouden en tikken op het scherm**: kies per gebaar afspelen/pauze, volgend of vorig nummer, volume, dempen, shuffle, herhalen, het Spotify-venster tonen of een playlist openen. Standaard: klik = afspelen/pauze, dubbelklik = volgend nummer, 3x klikken = vorig nummer, vasthouden = Spotify tonen of verbergen.
- **Draaien**: volume van Spotify of vorige/volgende. Ingedrukt draaien spoelt 5 seconden per stap, of doet het andere.
- **Stijlen**: Player (zoals de officiële dial), Picture (de hoes over het hele scherm) of Compact. Spotify-groen op de play-knop, de tijdlijn en/of als klein logo (aan te vinken) maakt de dial herkenbaar naast andere mediadials.
- **Lang tikken op het scherm**: links vorig nummer, rechts volgend nummer.
- Staat Spotify uit, dan start elke actie Spotify.

Werkt met de Spotify-app voor Windows, zonder Spotify-account of inlog in de plugin: alles gaat via de mediabediening van Windows. Is de officiële Spotify-plugin van Elgato ook geïnstalleerd, dan gebruikt de dial diens icoontjes, zodat hij er precies zo uitziet.

## PlayStation 5 for OBS

Voor wie een PlayStation 5 via een capturekaart met pass-through in OBS hoort.

- **PS5 Volume** (dial): draaien regelt hoe hard je de PlayStation hoort, ingedrukt draaien per 1%. Drukken of tikken zet mute aan of uit, of start OBS als die uit staat. Vasthouden zet het volume terug naar je standaard.
- De dial ziet eruit als de Spotify-dial: naam met **On** of **Off** eronder, linksonder Volume of Muted, rechtsonder het percentage en een PlayStation-blauwe balk. Staat de PlayStation uit, dan wordt alles grijs.
- Aan of uit leest de plugin af aan het geluid van de capturekaart. Staat obs-websocket aan (Extra > WebSocket-serverinstellingen), dan kijkt hij ook naar het beeld en herkent hij het "NO SIGNAL"-scherm van de kaart. Het wachtwoord leest de plugin zelf uit de OBS-instellingen op deze pc.

Het volume is OBS in de Windows-volumemixer: het verandert alleen wat jij hoort, niet je stream of opname. De bronnen heten standaard "PS5 beeld" en "PS5 geluid"; andere namen kies je in de instellingen van de dial.

## F1

Voor F1 kijken met [MultiViewer](https://multiviewer.app/download) (nodig) en [Ultimate F1 Viewer](https://github.com/MRAJEKO/UF1-Viewer-with-F1MV/releases/latest) (optioneel). Ontbreekt een van de twee, dan meldt de plugin dat en biedt de downloadpagina aan.

- **F1 Mode**: zet alles klaar op een eigen bureaublad "F1". Dubbelklikken verbergt en dempt alles, 1 seconde vasthouden sluit alles.
- **Race** / **Quali**: opent je MultiViewer-setup en Ultimate F1 Viewer-layout met die naam, voor de live sessie of anders de laatste replay. Vult onboards van coureurs buiten de topteams met de beste van de rest en kiest de coureurs in de battle mode.
- **Session**: resterende tijd of ronden, en het weer.
- **Pause**, **Go live**, **Sync**, **Fullscreen**: bediening van MultiViewer.
- Dials: **Live delay**, **Volume**, **Track status** (vlaggen op Philips Hue) en **Flag lights** (welke Hue-ruimte).

Philips Hue is optioneel: koppel je bridge in de instellingen van de Track status-dial. Zonder Hue staan die functies op "Not set up". Een eigen logo voor F1 Mode zet je als `f1-logo.svg` of `f1-logo.png` in `%APPDATA%\StanF1`.

**Privacy:** de plugin bewaart geen F1 TV- of MultiViewer-inlog en stuurt geen gegevens naar de maker. MultiViewer en Ultimate F1 Viewer gebruiken hun eigen inlog op je eigen pc. Instellingen, zoals de sleutel van je Hue Bridge en vensterposities, staan alleen lokaal in `%APPDATA%\StanF1`. Voor replays vraagt de plugin openbare gegevens op bij OpenF1 en F1 TV, zonder inlog. MultiViewer en Ultimate F1 Viewer worden gestart met een debugpoort (9334 en 9333) die alleen vanaf de eigen pc bereikbaar is.

Werkt op Windows met de Stream Deck-app en op Linux (x64) met [OpenDeck](https://github.com/nekename/OpenDeck). Het `F1.streamDeckPlugin`-bestand bevat beide versies; installeer het in OpenDeck via het tabblad Plugins.

| | Windows (Stream Deck) | Linux (OpenDeck) |
|---|---|---|
| Vlaggen op Hue, latency, volume, pauze, live, sync, fullscreen, Session | ja | ja |
| Race / Quali: MultiViewer-setups, Ultimate F1 Viewer-layouts, best of the rest, battle mode | ja | ja |
| Laatste replay zoeken (OpenF1, F1 TV) en openen in MultiViewer | ja | ja |
| MultiViewer en Ultimate F1 Viewer starten (met debugpoort) en sluiten | ja | ja |
| Eigen bureaublad "F1" | ja | nee, je blijft op het huidige werkblad |
| Vensters op hun plek zetten, stapelen op hoogte, battle mode vóór de vlaggenbalk | ja | nee, dat doet je windowmanager |
| Dubbelklik F1 Mode: alles minimaliseren | ja | nee (dempen werkt wel) |
| Melding als een app ontbreekt | venster met Ja/Nee | zenity of kdialog, anders een melding en de downloadpagina via `xdg-open` |

Op Linux vindt de plugin MultiViewer als .deb/.rpm (`multiviewer`), als Flatpak of als uitgepakte zip, en Ultimate F1 Viewer als AppImage in bijvoorbeeld `~/Applications` of `~/Downloads`, of via een snelkoppeling in `~/.local/share/applications`. Staat een app ergens anders, zet het pad dan als `MultiViewerPath` of `Uf1Path` in `~/.config/StanF1/settings.json`. Instellingen, het log en een eigen logo staan op Linux in `~/.config/StanF1`. De dials van de Stream Deck + hangen af van wat OpenDeck en je apparaat ondersteunen.

## Zelf bouwen

Vereist de .NET 10 SDK. Met `install.ps1` in `VolumeLauncher`, `Scarlett`, `Monitor`, `Spotify`, `PS5` of `F1` bouw en installeer je een plugin op deze pc. Elke `install.ps1` zet de update daarna ook op GitHub met een nieuwe release, via `publish.ps1` (bij `PS5` alleen met `-Publish`). Met `package.ps1` maak je alleen de installatiebestanden in `dist\`.

`Scarlett` gebruikt een paar bestanden uit `VolumeLauncher\src` (iconen, afbeeldingen laden, het log), zodat beide plugins er hetzelfde uitzien.

### Bijhouden wat er nieuw is

Zet bij elke wijziging een regel in [CHANGELOG.md](CHANGELOG.md) onder **Nog niet uitgebracht**, in de groep **Nieuw**, **Verbeterd** of **Opgelost**, met de naam van de plugin vooraan. `publish.ps1` zet die regels als releasenotes bij de nieuwe release en schuift ze in CHANGELOG.md onder het nieuwe versienummer.
