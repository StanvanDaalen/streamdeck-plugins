# Changelog

What is new, improved or fixed in each release. The newest is at the top.

Add a change under **Unreleased**, with the plugin name first. At the next release `publish.ps1` posts those lines as the release notes on GitHub and moves them below under the new version number.

## Unreleased

### New

### Improved

### Fixed

## 1.1.52 (2026-11-24)

### New

- **Volume & Launcher**: App Volume gesture **Fullscreen on / off**, for example on double-click: the video you are watching goes fullscreen, or back to its window.

### Fixed

- **Volume & Launcher**: Paramount+ showed only the series ("FBI") on the App Volume dial. When a site reports just the series, the episode and series now come from the browser tab ("Hero's Journey" with "FBI" below), which also brings back the episode picture.

## 1.1.51 (2026-11-23)

### New

- **PlayStation 5 for OBS**: press and turn the dial to set Discord's volume, so one dial controls both the game and your friends. The dial shows Discord's level meanwhile. The Discord step is adjustable, and the old 1% PlayStation steps are still available under **Pressed turn**.

### Improved

- **PlayStation 5 for OBS**: the settings are grouped into Status, Display, Volume, Pressed turn and OBS sources, with a switch for what a pressed turn does.

## 1.1.50 (2026-11-23)

### Fixed

- **Volume & Launcher**: choosing the app on App Key and App Volume works again: the drop-down that Stream Deck showed empty is now a list with a search field inside the settings.

## 1.1.49 (2026-11-23)

### New

- **Volume & Launcher**: App Key shows what its app plays, like the App Volume dial: picture, title, series or artist, timeline, and the service's colour and small logo. Click, double-click and hold can each be set to minimize/open, play/pause, next, previous, mute or close.

## 1.1.48 (2026-11-23)

### New

- **Volume & Launcher**: the Stream Deck Now Playing Chrome extension now comes with the plugin. App Volume's settings have a Chrome extension section with Open extension folder, Copy folder path and the steps to load it, and warn when Chrome reports only a service's name.

## 1.1.47 (2026-11-23)

### Fixed

- **Volume & Launcher**: App Volume recognises a service from its own title first, so Paramount+ is not taken for Netflix when a Netflix tab is in front, and follows the Chrome profile that actually plays.

## 1.1.46 (2026-11-23)

### Fixed

- **Volume & Launcher**: App Volume keeps the streaming service's colour and small logo when you pause or switch tabs, instead of briefly losing them.
- **Stream Deck Now Playing** (Chrome extension): finds the Netflix episode again (Netflix moved the address it reads it from).

## 1.1.45 (2026-11-23)

### New

- **Volume & Launcher**: on F1 TV, MotoGP and WEC, App Volume shows the race on top (for example Spanish GP) and the session below (Practice 1, Qualifying, Race, …), with the flag of the weekend's country as the picture.
- **Stream Deck Now Playing** (Chrome extension): now works on every supported streaming service, not only Netflix.

## 1.1.44 (2026-11-23)

### New

- **Stream Deck Now Playing** (Chrome extension): passes the Netflix episode, series and picture on to Windows, so App Volume shows them.

### Fixed

- **Volume & Launcher**: App Volume finds the streaming service from the browser tab first, so an episode named for example "Max" is not mistaken for HBO Max.

## 1.1.43 (2026-11-23)

### New

- **Volume & Launcher**: App Volume shows a picture of the episode for series whose service gives none (Paramount+, Netflix), looked up on TVmaze. It can be turned off under Streaming.

### Fixed

- **Volume & Launcher**: App Volume splits titles such as "Watch Sheriff Country Season 1 Episode 8: Death & Taxes - Paramount+" into the episode on top and the series below.

## 1.1.42 (2026-11-23)

### New

- **Volume & Launcher**: App Volume recognises Netflix, HBO Max, Paramount+, Viaplay, Videoland, SkyShowtime, F1 TV, MotoGP, WEC, Disney+, Apple TV and Prime Video, in a browser or in their own app. The dial then shows the episode on top, the series below, and the service's colour and small logo, like YouTube.

## 1.1.41 (2026-11-23)

### Fixed

- **F1 Race Control**: Hide and End session switch desktops again on PCs where Windows keeps the current desktop per sign-in session (Windows 10 and some Windows 11 builds); the plugin thought it was always on desktop 1.
- **F1 Race Control**: after the plugin restarted (OpenDeck does that more often), Hide still finds the F1 desktop and End session still closes Ultimate F1 Viewer and goes back to desktop 1.
- **F1 Race Control**: End session with F1 Party also closes Ultimate F1 Viewer, the flags on the lights and the F1 desktop, not only F1 Party's session.

## 1.1.40 (2026-11-22)

### Improved

- **F1 Race Control**: Stream Deck doesn't let a plugin open a page of your own profile, so the Back key's settings explain the way that works: Stream Deck's own Go to Page key, with the plugin's back arrow as its image (in the middle of the key, without text) and its title (such as PAGE 1) on or off.

## 1.1.39 (2026-11-22)

### Improved

- **F1 Race Control**: the Back arrow is all white.

## 1.1.38 (2026-11-22)

### New

- **F1 Race Control**: an **F1 Race Control profile** for the Stream Deck +, the Stream Deck (15 keys) and the Stream Deck XL comes with the plugin. Put the new **F1 profile** key on your own page: the first press asks to install the profile that fits your Stream Deck and opens it. Its **Back** key takes you back to the profile you came from.

### Improved

- **F1 Race Control**: with F1 Party, where F1 opens (its own desktop and its name, or the current desktop) follows F1 Party's setting, for Race, Quali, Session and Hide alike.
- **F1 Race Control**: the Party key shows how many watch inside its screen, in the sync colour (green, amber, red), with one person under it per viewer (up to four), instead of a tag in the corner.
- **F1 Race Control**: cleaner Replay and Invite icons.
- **F1 Race Control**: Hide / Show has a clearer icon: a screen with an arrow down (hide), grey with an arrow up (show).
- **F1 Race Control**: **Hide** with F1 on its own desktop switches to your first desktop (where F1 Party is) and back, instead of minimising everything.
- **F1 Race Control**: status tags (such as ON on Flags) are small rounded labels top left with room around them; the icon makes way. Fullscreen no longer shows ON next to Exit full.
- **F1 Race Control**: End session is now **Start / end session**: with no session open it shows a calendar and opens F1 Party on its calendar to pick one; with a session open it ends it.

### Fixed

- **F1 Race Control**: the Track status dial's image in the Stream Deck app is the plain white flag, like the other dials, instead of the old Flags key.

- **F1 Race Control**: the Party key never shows 0 while you are in a party (you count yourself), also in the first seconds after starting one.

- **F1 Race Control**: F1's own desktop is found again after it was made (its name is written where Windows keeps it right away), so starting a session makes at most one desktop and Hide switches to it instead of minimising.

- **F1 Race Control**: the first desktop never counts as F1's own desktop, so F1 doesn't open there when that desktop happens to carry F1's name.

## 1.1.36 (2026-11-22)

### New

- **F1 Race Control**: three keys to swap in when you like: **Replay** (10 seconds back; onboards and, as the host, the whole party follow), **Hide** (minimise F1 and mute everything, press again for it all back) and **Invite** (send the F1 Party invite to Discord).
- **F1 Race Control**: **Start together** key: F1 Party's five start lights for the whole party, then everyone plays at the same moment. The lights on the key come on one by one during the countdown.
- **F1 Race Control**: the Sync key is now the **Party** key for F1 Party, with its own icon (four people in front of a screen): bright in a party with how many watch, dark with a stroke when you're not in one. Press: start a party, or sync when you're in one (as the host everyone syncs to you). Double-press: copy the party code. Triple-press: send the Discord invite. Hold: leave the party. Without F1 Party a press still syncs your streams.

### Improved

- **F1 Race Control**: with F1 Party installed, Race, Quali, Session and Close F1 tell F1 Party what to do, and F1 Party opens and closes everything the way its "Open with" says. Without F1 Party they work as before.
- **F1 Race Control**: **Close F1 is now End session**, with a tidier power icon. With F1 Party it ends the session there (the party stays).
- **F1 Race Control**: the Track status dial shows a plain white flag, like the other dials; the flag colour stays in the text and the bar.
- **F1 Race Control**: F1 Mode, Pause / play and Go live are no longer in the action list (Race, Quali and the Live delay dial cover them). Keys you already placed keep working.

## 1.1.35 (2026-11-22)

### New

- **F1 Race Control**: **Fit Live Timing to its window** in the Race and Quali settings (on by default). Turn it off and the Live Timing keeps MultiViewer's own size and replay bar; the change applies to the open window straight away.

### Improved

- **F1 Race Control**: the Copy setup prompt button and its prompt file are gone from the Race and Quali settings.

## 1.1.34 (2026-11-22)

### New

- **F1 Race Control**: choose what happens to the Windows taskbar while F1 runs (F1 Mode settings): on the main screen only, hide automatically, or leave it as it is. When F1 closes it goes back to how it was. The old checkbox carries over: on becomes main screen only, off becomes leave as it is.

## 1.1.33 (2026-11-22)

### New

- **F1 Race Control**: a **Close F1** key that closes everything with one press, the same as holding Race, Quali or F1 Mode for 1 second. Handy for anyone who does not know the hold.

## 1.1.32 (2026-11-22)

### Fixed

- **F1 Race Control**: the time left on the Session key no longer stands still (for example at 3:09 in qualifying): MultiViewer's timing clock is now counted on between its updates, and whole seconds round up like the F1 clock.

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
