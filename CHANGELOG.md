# Changelog

What is new, improved or fixed in each release. The newest is at the top.

Add a change under **Unreleased**, with the plugin name first. At the next release `publish.ps1` posts those lines as the release notes on GitHub and moves them below under the new version number.

## Unreleased

### New

### Improved

### Fixed

## 1.1.31 (2026-11-22)

### New

- **F1 Race Control**: a **Copy setup prompt** button in the Race and Quali key settings: a prompt for an AI assistant that builds your own MultiViewer setups and Ultimate F1 Viewer layouts per session type, fitted to your monitors and checked with a yellow desktop.

### Fixed

- **F1 Race Control**: the key settings showed every option as off: a quote in a help text broke the settings panel (since 1.16.3). `install.ps1` now checks the panel script before installing.

## 1.1.30 (2026-11-22)

### New

- **F1 Race Control**: Race and Quali start where you left off in that session, else 1 minute before the start, also when the session is already running (you watch it from the start instead of halfway). Setting "Start where I left off, else 1 minute before the start", on by default.

### Improved

- **F1 Race Control**: the Session key has more pages for every session: weather (track and air temperature, dry or rain), wind (speed, direction, humidity), chance of rain in the next hour (live sessions, from Open-Meteo), the leader (race: gap to P2; other sessions: best time and gap), the fastest lap (race) and safety cars / red flags (when there were any). Back to laps or time left after 30 seconds.

### Fixed

- **F1 Race Control**: Race and Quali open the latest replay again right after a live session: the calendar is kept on disk (OpenF1 refuses everyone while a session is live) and, without a calendar, the weekend MultiViewer just showed is used; the start point only moves when the right session is open.
- **F1 Race Control**: best of the rest no longer reopens two onboards when their drivers only change places; a new onboard starts at the moment you are watching instead of the start of the broadcast ("no video"); during the race at most one swap every 5 laps, after the change has held for 3 laps.

## 1.1.29 (2026-11-22)

### New

- **F1 Race Control**: in an F1 Party the Sync key shows how many watch (green: everyone in sync, amber: someone isn't yet, red: no connection) and the party code; "SYNC ALL" and HOST when you host.

## 1.1.28 (2026-11-22)

### New

- **F1 Race Control**: works with [F1 Party](https://github.com/Jeremy0538/f1party), the app for watching F1 in sync with friends. When you join a party, the plugin opens the host's session with your own desktop, setup and Ultimate F1 Viewer layout (local only, on 127.0.0.1:9335; web pages can't use it).
- **F1 Race Control**: Sync key with F1 Party. Press: all streams jump to your main screen (as a guest: first to the host). Hold 1 second: starts an F1 Party and sends the invite to Discord; already hosting: everyone syncs to you and the invite is sent again.
- **F1 Race Control**: Pause key with F1 Party: the whole party pauses and plays with you; with "Pause everyone" off only you pause and the party plays on.
- **F1 Race Control**: also runs on Windows with OpenDeck instead of the Stream Deck app (`install.ps1 -OpenDeck`).

### Improved

- **F1 Race Control**: clearer settings for every key: one line per action at the top, each setting with its own short note, on/off switches instead of tick boxes, and tooltips in one style.

## 1.1.27 (2026-11-21)

### Improved

- **F1 Race Control**: the Live Timing window fills its whole screen: after opening a layout its replay bar (which only shows on mouse-over) is hidden and the rows are zoomed to reach the bottom, instead of leaving an empty strip below the last driver.

## 1.1.26 (2026-11-21)

### Improved

- **F1 Race Control**: the chequered flag on the Hue lights pulses slowly instead of flashing on and off, and shows once per finish. In qualifying and practice it stays until the last car is back in the pits (at most 5 minutes); in the race it lasts about 12 seconds.

## 1.1.25 (2026-11-21)

### New

- **F1 Race Control**: free practice gets its own layout. F1 Mode (automatic) and the Session key open the session that is on track with its own MultiViewer setup and Ultimate F1 Viewer layout: practice, qualifying or race (sprints included). The setup and layout are found by name ("Practice" or "Training") unless chosen in the settings.
- **F1 Race Control**: the Session key opens the session that is on track when you press it; with nothing on track it still switches between clock and weather.

### Improved

- **F1 Race Control**: best of the rest swaps more calmly. In the race a new order has to hold for a full lap, with at most one swap per lap and none under a safety car or red flag; in qualifying a slot only changes once its driver is back in the pits; in practice the slots keep their drivers.
- **F1 Race Control**: every stream opens with the whole picture (no cropping).

### Fixed

- **F1 Race Control**: F1 Mode on automatic opened the race layout during a practice session.
- **F1 Race Control**: switching between Race, Quali and Practice closes the timing windows the new setup does not have, instead of leaving them on top of the new layout.

## 1.1.24 (2026-11-21)

### Improved

- **F1 Race Control** and **Better Spotify Dial**: the icon in the Stream Deck action list is white, like the other plugins.

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
