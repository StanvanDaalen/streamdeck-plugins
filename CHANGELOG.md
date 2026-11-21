# Changelog

Wat er per release nieuw, verbeterd of opgelost is. De nieuwste staat bovenaan.

Zet een wijziging onder **Nog niet uitgebracht**, met de naam van de plugin vooraan. Bij de volgende release zet `publish.ps1` die regels als releasenotes op GitHub en schuift ze hieronder onder het nieuwe versienummer.

## Nog niet uitgebracht

### Nieuw

### Verbeterd

### Opgelost

## 1.1.17 (2026-11-21)

### Nieuw

- **PlayStation 5 for OBS**: een klein PlayStation-logo (de console) op de hoek van het vakje, zoals het YouTube- en Spotify-logo op de mediadials. In de instellingen zet je dat logo en de PlayStation-blauwe balk elk aan of uit; staat de balk uit, dan blijft hij wit.

## 1.1.16 (2026-11-21)

**Overstappen**: installeer Volume & Launcher over Audio heen; je dials blijven werken. Verwijder de oude Launcher-plugin en zet je Game- en App-knoppen opnieuw neer. Gebruik je de Scarlett-knoppen, installeer dan ook Scarlett Switch en zet die opnieuw neer.

### Nieuw

- **Volume & Launcher**: Audio en Launcher zijn één plugin. De Game Volume- en App Volume-dials en de Game Key- en App Key-knoppen delen nu één gamebibliotheek en één gamedetectie, dus een eigen naam of logo zie je meteen overal.
- **Scarlett Switch**: de Scarlett-knoppen en -dial zijn een eigen plugin, voor wie een Focusrite Scarlett heeft. Met dezelfde opties als voorheen.
- **App Key**: een eigen icoon (een venster) in plaats van een luidspreker.

### Verbeterd

- **Volume & Launcher**: de game wordt nog maar één keer gezocht in plaats van door twee plugins tegelijk.
- **README**: per plugin een volledige uitleg van wat hij doet en alle opties.
- **Releases**: elke release krijgt de lijst Nieuw, Verbeterd en Opgelost uit deze changelog.

### Opgelost

- **Volume & Launcher**: een ongebruikt icoon van de oude game-knop is weg.

## 1.1.15 (2026-11-21)

- **PlayStation 5 for OBS**: linksonder een luidspreker-icoon (met kruisje bij mute) in plaats van de tekst Volume/Muted. De geluidsbron wordt ook gevonden als die niet in de actieve scèneverzameling van OBS zit.

## 1.1.14 (2026-11-21)

- **Audio – App Volume**: speelt de app niets af, dan toont de dial de app in dezelfde Player-stijl als de mediadials (Classic blijft kiesbaar). Het volume is dan al in te stellen.
- **Audio – Scarlett Switch**: Player-weergave met eigen apparaatnamen.

## 1.1.13 (2026-11-21)

- **Audio – App Volume**: staat de app dicht, dan toont de dial "Open Chrome" met het app-logo; een klik start de app.
- **App-kenmerk** (Audio en Better Spotify Dial): play-knop, tijdlijn en klein logo zijn losse vinkjes.

## 1.1.12 (2026-11-21)

- Nieuw: **PlayStation 5 for OBS**, een dial voor het geluid van je PS5 via OBS en een capturekaart: volume, mute, OBS starten en zien of de PS5 aan staat.

## 1.1.11 (2026-11-20)

- **Audio – App Volume**: titel, kanaal, plaatje, afspelen/pauze en tijdlijn als de app iets afspeelt, in de stijl Player, Picture of Compact. Driemaal klikken = vorig nummer, ingedrukt draaien = spoelen.
- **Better Spotify Dial**: dezelfde drie stijlen, spoelen en Spotify-groen als herkenning.

## 1.1.10 (2026-11-20)

- **Better Spotify Dial**: blijft niet meer hangen als de mediabediening van Windows niet reageert.

## 1.1.9 (2026-11-20)

- Nieuw: **Better Spotify Dial**, Spotify op een dial met eigen acties voor 1x, 2x en 3x klikken, vasthouden en tikken.

## 1.1.8 (2026-11-20)

- Alle plugins in het Engels.
- **Audio**: App Volume kan afspelen/pauzeren en een nummer verder of terug; de Game Volume-dial kan bij één game horen.
- **Launcher**: Game Key en Fixed Game Key zijn één Game Key.
- **Monitor**: stijlen Basic, Tile en Icon only, PC- en PS5-apparaten, schermen wakker houden en een ander hoofdscherm zolang de PS5 in beeld is.

## 1.1.7 (2026-11-20)

- **F1 1.13.9**: Race en Quali openen weer de juiste sessie met MultiViewer 2.17; alle spelers in een setup starten goed; alleen de timing van de geopende sessie telt; knoppen weten van elkaar of F1 al open staat; taakbalk alleen op het hoofdscherm; geen naden tussen vensters; de volume-dial regelt de stream op het hoofdscherm; de sessieknop toont de volgende sessie.

## 1.1.6 (2026-11-14)

- **Monitor**: optie om de monitor in Windows los te koppelen zolang hij op een andere input staat. LG-inputwissel probeert opnieuw als de driver de eerste keer weigert.

## 1.1.5 (2026-11-14)

- **Launcher**: logo kiezen ook bij de game-knop, voor de game die nu open staat.
- **Audio**: de Gamebibliotheek kiest de game die nu speelt; gekozen logo's worden blijvend bewaard.

## 1.1.4 (2026-11-14)

- **Launcher**: eigen logo per game, blijvend bewaard.
- Opgelost: anti-cheatprogramma's (_EAC) worden niet meer als game gezien; Audio en Launcher overschrijven elkaars namen en logo's niet meer.

## 1.1.3 (2026-11-14)

- **Monitor**: PC- en PS5-icoon in de icoonbibliotheek.

## 1.1.2 (2026-11-14)

- **Monitor**: LG-monitors wisselen van input via NVIDIA; modelnaam uit de EDID.

## 1.1.1 (2026-11-12)

- **F1 1.13**: de Session-knop toont de eerstvolgende sessie met afteller; Race en Quali tonen welke sessie ze openen.

## 1.1.0 (2026-11-11)

- Nieuw: de **F1**-plugin voor MultiViewer en Ultimate F1 Viewer, ook op Linux met OpenDeck.

## 1.0.2 (2026-11-09)

- Rustige gamenamen, schakelaars voor naam en speeltijd, menu's geordend.

## 1.0.1 (2026-11-09)

- Lange gamenamen schuiven af en toe opzij; updates gaan automatisch naar GitHub.

## 1.0.0 (2026-11-09)

- Eerste release: **Audio** (volume per game en app, Scarlett Switch), **Launcher** (knoppen voor games en apps, met speeltijd) en **Monitor** (wisselen tussen monitorinputs).
