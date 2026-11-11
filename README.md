# Stream Deck-plugins

Eigen plugins voor de Elgato Stream Deck op Windows. De F1-plugin werkt ook op Linux met OpenDeck.

## Installeren

1. Open de [nieuwste release](https://github.com/StanvanDaalen/streamdeck-plugins/releases/latest).
2. Download `Audio.streamDeckPlugin`, `Launcher.streamDeckPlugin`, `Monitor.streamDeckPlugin` en/of `F1.streamDeckPlugin`. Launcher gebruikt de gamebibliotheek van Audio voor eigen namen en afbeeldingen, maar werkt ook los.
3. Dubbelklik op het bestand en bevestig de installatie in Stream Deck.

Op Linux (alleen F1): installeer [OpenDeck](https://github.com/nekename/OpenDeck), open daarin het tabblad **Plugins** en kies het gedownloade `F1.streamDeckPlugin`.

.NET hoeft niet geïnstalleerd te zijn. De plugins zijn niet gesigneerd, dus Windows of Stream Deck kan een waarschuwing tonen.

## Audio

- **Game Volume**: dial die de open game herkent en het volume regelt, met naam en logo.
- **App Volume**: dial voor een vaste app, zoals Discord of Spotify.
- **Scarlett Switch**, **Scarlett Speakers**, **Scarlett Headphones**: wisselen tussen speakers en koptelefoon. Dit vereist een Focusrite Scarlett, Focusrite Control 2 en een MIDI-loopbackpoort.

Games worden automatisch gevonden via Steam, Epic, Xbox en Lunar Client. Andere games voeg je toe in de Gamebibliotheek.

## Launcher

- **Game-knop**: de game die nu open staat, met logo, naam en speeltijd.
- **Vaste game-knop**: hetzelfde voor één gekozen game, die de knop ook kan starten.
- **Applicatie-knop**: hetzelfde voor een app.

Klik om te minimaliseren, weer te geven of te starten. Vasthouden sluit af.

## Monitor

- **Monitor Input**: wissel met een knop tussen de inputs van een monitor. De monitor moet DDC/CI ondersteunen.

Vink in het menu alleen inputs aan waar iets op is aangesloten. Een monitor geeft niet door welke inputs signaal hebben.

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

Vereist de .NET 10 SDK. Met `install.ps1` in `GameVolume`, `Launcher`, `Monitor` of `F1` bouw en installeer je een plugin op deze pc. Elke `install.ps1` zet de update daarna ook op GitHub met een nieuwe release, via `publish.ps1`. Met `package.ps1` maak je alleen de installatiebestanden in `dist\`.
