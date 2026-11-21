# Stream Deck-plugins

Eigen plugins voor de Elgato Stream Deck op Windows. De F1-plugin werkt ook op Linux met OpenDeck.

## Installeren

1. Open de [nieuwste release](https://github.com/StanvanDaalen/streamdeck-plugins/releases/latest).
2. Download `Audio.streamDeckPlugin`, `Launcher.streamDeckPlugin`, `Monitor.streamDeckPlugin`, `BetterSpotifyDial.streamDeckPlugin`, `PlayStation5-OBS.streamDeckPlugin` en/of `F1.streamDeckPlugin`. Launcher gebruikt de gamebibliotheek van Audio voor eigen namen en afbeeldingen, maar werkt ook los.
3. Dubbelklik op het bestand en bevestig de installatie in Stream Deck.

Op Linux (alleen F1): installeer [OpenDeck](https://github.com/nekename/OpenDeck), open daarin het tabblad **Plugins** en kies het gedownloade `F1.streamDeckPlugin`.

.NET hoeft niet geïnstalleerd te zijn. De plugins zijn niet gesigneerd, dus Windows of Stream Deck kan een waarschuwing tonen.

## Audio

- **Game Volume**: dial die de open game herkent en het volume regelt, met naam en logo.
- **App Volume**: dial voor een vaste app, zoals Discord of Chrome. Speelt de app iets af, bijvoorbeeld een YouTube-video, dan toont de dial titel, kanaal, plaatje, afspelen/pauze en een tijdlijn, in de stijl Player, Picture of Compact. Klik, dubbel- en driemaal klikken: afspelen/pauze, volgend en vorig nummer. Ingedrukt draaien spoelt 5 seconden per stap. Een browser is te herkennen aan YouTube-rood: vink aan of dat op de play-knop, de tijdlijn of als klein logo verschijnt, los of samen. Staat de app dicht, dan toont de dial "Open Chrome" en start een klik de app.
- **Scarlett Switch**, **Scarlett Speakers**, **Scarlett Headphones**: wisselen tussen speakers en koptelefoon. Dit vereist een Focusrite Scarlett, Focusrite Control 2 en een MIDI-loopbackpoort.

Games worden automatisch gevonden via Steam, Epic, Xbox en Lunar Client. Andere games voeg je toe in de Gamebibliotheek.

## Launcher

- **Game-knop**: de game die nu open staat, met logo, naam en speeltijd.
- **Vaste game-knop**: hetzelfde voor één gekozen game, die de knop ook kan starten.
- **Applicatie-knop**: hetzelfde voor een app.

Klik om te minimaliseren, weer te geven of te starten. Vasthouden sluit af.

Bij de game-knop en de vaste game-knop kies je onder Logo een eigen afbeelding (PNG, JPG of SVG) voor die game; de game-knop past het logo aan van de game die nu open staat. In de Gamebibliotheek van de Game Volume-dial staat de game die nu speelt bovenaan en is die al gekozen. De plugin bewaart een kopie in %APPDATA%\StanGameVolume\logos, dus het logo blijft staan als je het originele bestand verplaatst. Het logo geldt voor alle knoppen en dials van die game.

## Monitor

- **Monitor Input**: wissel met een knop tussen de inputs van een monitor. De monitor moet DDC/CI ondersteunen.

Vink in het menu alleen inputs aan waar iets op is aangesloten. Een monitor geeft niet door welke inputs signaal hebben.

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

Vereist de .NET 10 SDK. Met `install.ps1` in `GameVolume`, `Launcher`, `Monitor`, `Spotify`, `PS5` of `F1` bouw en installeer je een plugin op deze pc. Elke `install.ps1` zet de update daarna ook op GitHub met een nieuwe release, via `publish.ps1` (bij `PS5` alleen met `-Publish`). Met `package.ps1` maak je alleen de installatiebestanden in `dist\`.
