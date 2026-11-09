# Stream Deck-plugins

Eigen plugins voor de Elgato Stream Deck op Windows.

## Installeren

1. Log in op GitHub en open de pagina **Releases** van deze repository.
2. Download `Audio.streamDeckPlugin`, `Launcher.streamDeckPlugin` en/of `Monitor.streamDeckPlugin`. Launcher gebruikt de gamebibliotheek van Audio voor eigen namen en afbeeldingen, maar werkt ook los.
3. Dubbelklik op het bestand en bevestig de installatie in Stream Deck.

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

## Zelf bouwen

Vereist de .NET 10 SDK. Met `install.ps1` in `GameVolume`, `Launcher` of `Monitor` bouw en installeer je een plugin op deze pc. Elke `install.ps1` zet de update daarna ook op GitHub met een nieuwe release, via `publish.ps1`. Met `package.ps1` maak je alleen de installatiebestanden in `dist\`.
