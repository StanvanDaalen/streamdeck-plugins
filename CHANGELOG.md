# Changelog

What is new, improved or fixed in each release. The newest is at the top.

Add a change under **Unreleased**, with the plugin name first. At the next release `publish.ps1` posts those lines as the release notes on GitHub and moves them below under the new version number.

## Unreleased

### New

### Improved

### Fixed

## 1.1.23 (2026-11-21)

### Improved

- **F1 Race Control**: the F1 plugin is now called F1 Race Control in Stream Deck. Your keys and settings stay as they are.

## 1.1.22 (2026-11-21)

### New

- **F1**: your own logo in `%APPDATA%\StanF1` (`f1-logo.svg` or `.png`) now also replaces the plugin, category and F1 Mode icons when you install on your own PC.

## 1.1.21 (2026-11-21)

### Improved

- **F1**: menus, tooltips and messages are in English.

## 1.1.20 (2026-11-21)

### Improved

- **Better Spotify Dial**: while you change the volume the bar is now white (grey when muted), the same as App Volume. Green stays for the timeline, so you can always tell volume and timeline apart.

## 1.1.19 (2026-11-21)

### New

- **Volume & Launcher**: App Volume dials have a **Seek step** setting: hold and turn rewinds or fast-forwards 5, 10, 15, 30 or 60 seconds per step.

## 1.1.18 (2026-11-21)

### Improved

- **PlayStation 5 for OBS**: the small logo in the corner of the tile is now the PlayStation logo (our own drawing) instead of the shape of the console.
- **GitHub**: README, changelog and release notes are in English.

## 1.1.17 (2026-11-21)

### New

- **PlayStation 5 for OBS**: a small PlayStation logo (the console) in the corner of the tile, like the YouTube and Spotify logos on the media dials. In the settings you turn that logo and the PlayStation-blue bar on or off separately; with the bar off, it stays white.

## 1.1.16 (2026-11-21)

**Upgrading**: install Volume & Launcher over Audio; your dials keep working. Remove the old Launcher plugin and place your Game and App keys again. If you use the Scarlett keys, also install Scarlett Switch and place those again.

### New

- **Volume & Launcher**: Audio and Launcher are one plugin. The Game Volume and App Volume dials and the Game Key and App Key keys now share one game library and one game detection, so a custom name or logo shows up everywhere right away.
- **Scarlett Switch**: the Scarlett keys and dial are a plugin of their own, for anyone with a Focusrite Scarlett. Same options as before.
- **App Key**: its own icon (a window) instead of a speaker.

### Improved

- **Volume & Launcher**: the open game is detected once instead of by two plugins at the same time.
- **README**: a full description of every plugin and all its options.
- **Releases**: every release lists New, Improved and Fixed from this changelog.

### Fixed

- **Volume & Launcher**: an unused icon of the old game key is gone.

## 1.1.15 (2026-11-21)

- **PlayStation 5 for OBS**: a speaker icon bottom left (crossed out when muted) instead of the text Volume/Muted. The audio source is also found when it is not in the active OBS scene collection.

## 1.1.14 (2026-11-21)

- **Audio – App Volume**: when the app plays nothing, the dial shows the app in the same Player style as the media dials (Classic can still be chosen). The volume can already be set.
- **Audio – Scarlett Switch**: Player look with your own device names.

## 1.1.13 (2026-11-21)

- **Audio – App Volume**: when the app is closed, the dial shows "Open Chrome" with the app logo; a click starts the app.
- **App indicator** (Audio and Better Spotify Dial): play button, timeline and small logo are separate checkboxes.

## 1.1.12 (2026-11-21)

- New: **PlayStation 5 for OBS**, a dial for your PS5's sound through OBS and a capture card: volume, mute, start OBS and see whether the PS5 is on.

## 1.1.11 (2026-11-20)

- **Audio – App Volume**: title, channel, picture, play/pause and timeline while the app plays something, in the style Player, Picture or Compact. Triple-click = previous track, pressed turn = seek.
- **Better Spotify Dial**: the same three styles, seeking and Spotify green as indicator.

## 1.1.10 (2026-11-20)

- **Better Spotify Dial**: no longer hangs when Windows' media controls do not respond.

## 1.1.9 (2026-11-20)

- New: **Better Spotify Dial**, Spotify on a dial with your own actions for click 1x, 2x and 3x, hold and tap.

## 1.1.8 (2026-11-20)

- All plugins in English.
- **Audio**: App Volume can play/pause and skip to the next or previous track; the Game Volume dial can belong to one game.
- **Launcher**: Game Key and Fixed Game Key are one Game Key.
- **Monitor**: Basic, Tile and Icon only styles, PC and PS5 devices, keeping the displays awake and a different main display while the PS5 is on screen.

## 1.1.7 (2026-11-20)

- **F1 1.13.9**: Race and Quali open the right session again with MultiViewer 2.17; all players in a setup start correctly; only the timing of the open session counts; keys know whether F1 is already open; taskbar only on the main display; no seams between windows; the volume dial controls the stream on the main display; the session key shows the next session.

## 1.1.6 (2026-11-14)

- **Monitor**: option to disconnect the monitor in Windows while it is on another input. LG input switching retries when the driver refuses the first time.

## 1.1.5 (2026-11-14)

- **Launcher**: choose a logo on the game key too, for the game that is open right now.
- **Audio**: the Game library selects the game you are playing; chosen logos are kept permanently.

## 1.1.4 (2026-11-14)

- **Launcher**: a custom logo per game, kept permanently.
- Fixed: anti-cheat programs (_EAC) are no longer seen as a game; Audio and Launcher no longer overwrite each other's names and logos.

## 1.1.3 (2026-11-14)

- **Monitor**: PC and PS5 icons in the icon library.

## 1.1.2 (2026-11-14)

- **Monitor**: LG monitors switch input through NVIDIA; model name from the EDID.

## 1.1.1 (2026-11-12)

- **F1 1.13**: the Session key shows the next session with a countdown; Race and Quali show which session they open.

## 1.1.0 (2026-11-11)

- New: the **F1** plugin for MultiViewer and Ultimate F1 Viewer, also on Linux with OpenDeck.

## 1.0.2 (2026-11-09)

- Calmer game names, switches for name and playtime, tidier menus.

## 1.0.1 (2026-11-09)

- Long game names scroll now and then; updates go to GitHub automatically.

## 1.0.0 (2026-11-09)

- First release: **Audio** (volume per game and app, Scarlett Switch), **Launcher** (keys for games and apps, with playtime) and **Monitor** (switch between monitor inputs).
