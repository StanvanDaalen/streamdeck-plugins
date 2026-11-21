# Stream Deck plugins

Plugins for the Elgato Stream Deck on Windows. The F1 plugin also runs on Linux with OpenDeck.

## What's included

| Plugin | File | What it's for |
|---|---|---|
| [Volume & Launcher](#volume--launcher) | `VolumeLauncher.streamDeckPlugin` | Volume dials for games and apps, and keys that start, hide and close games and apps |
| [Scarlett Switch](#scarlett-switch) | `ScarlettSwitch.streamDeckPlugin` | Switch a Focusrite Scarlett between speakers and headphones |
| [Monitor](#monitor) | `Monitor.streamDeckPlugin` | Switch between the inputs of a monitor |
| [Better Spotify Dial](#better-spotify-dial) | `BetterSpotifyDial.streamDeckPlugin` | Spotify on a dial, with your own action for every click |
| [PlayStation 5 for OBS](#playstation-5-for-obs) | `PlayStation5-OBS.streamDeckPlugin` | Volume of a PS5 you hear through OBS and a capture card |
| [F1 Race Control](#f1-race-control) | `F1.streamDeckPlugin` | Watching F1 with MultiViewer, also on Linux |

What is new or improved in each version is listed in [CHANGELOG.md](CHANGELOG.md) and with every [release](https://github.com/StanvanDaalen/streamdeck-plugins/releases).

## Installing

1. Open the [latest release](https://github.com/StanvanDaalen/streamdeck-plugins/releases/latest).
2. Download the plugins you want (see the table above).
3. Double-click the file and confirm the installation in Stream Deck.

On Linux (F1 only): install [OpenDeck](https://github.com/nekename/OpenDeck), open its **Plugins** tab and choose the downloaded `F1.streamDeckPlugin`.

.NET does not need to be installed. The plugins are not signed, so Windows or Stream Deck may show a warning.

**Coming from an older version?** Audio and Launcher are now one plugin, **Volume & Launcher**, and the Scarlett keys are in the separate **Scarlett Switch** plugin. Install Volume & Launcher over Audio: your Game Volume and App Volume dials keep working. Then remove the old Launcher plugin in Stream Deck and place your Game and App keys again from the Volume & Launcher category. If you use the Scarlett keys, also install Scarlett Switch and place those keys again.

## Volume & Launcher

Volume dials for the game you are playing and for any app, and keys to start, hide and close games and apps. Everything shares one game library: a custom name or logo for a game shows up right away on every dial and key of that game.

Games are found automatically through Steam, Epic, Xbox and Lunar Client. You can add other games yourself in the Game library.

### Game Volume (dial)

Controls the volume of the game that is open right now, with that game's name and logo. Choose one game under **Game** and the dial always belongs to that game; while it is closed, the dial shows it greyed out.

- **Turn**: volume, in steps of 1, 2, 3, 5 or 10% (**Volume step**).
- **Click, double-click, triple-click and hold** (also tap and long touch on the screen): for each gesture choose mute, minimize/open, close, favorite volume or nothing. Default: click = mute, double-click = minimize/open, hold = close.
- **Pressed turn**: with **Game/Discord balance** on, you shift the volume between the game and Discord: the game louder and Discord quieter, or the other way round.
- **Display**: the game name or your own text as title; the logo as a small icon or as the background; the volume bar in white or in the game's colour.
- **Favorite volume**: set a fixed volume per game, or take the current one. With **Start games at favorite volume** the game always starts at that volume.
- **Game options** (apply to all Game Volume dials):
  - **Remember volume per game**: every game starts at the volume it had last time.
  - **Game/Discord balance**: see above.
  - **Lower Spotify while gaming**: Spotify goes down (to 10 to 80%) while a game is open, and back up afterwards.
- **Game library**: pick a game and give it your own name or image, mark something as **Not a game** so it no longer counts as a game, or remove a game you added yourself. Under **Add game** you add a game with a name and its .exe. The game you are playing is at the top and already selected.

### App Volume (dial)

Controls the volume of one app, such as Discord, Chrome or Spotify.

- **Turn**: volume. While the app plays something, a **pressed turn** rewinds or fast-forwards per step, by 5, 10, 15, 30 or 60 seconds (**Seek step**, 5 by default).
- **Click, double-click, triple-click and hold**: the same choices as Game Volume, plus play/pause, next track and previous track. Default: click = mute, double-click = minimize/open, triple-click = previous track, hold = close.
- **While playing**: when the app plays something, for example a YouTube video, the dial shows the title, channel, picture, play/pause and a timeline. Styles: **Player**, **Picture** (the picture across the whole screen), **Compact** or **Off** (normal display). A short turn briefly shows the volume.
- **App indicator**: so you can tell the dial apart from the Spotify dial, a browser gets YouTube red and Spotify gets green. Tick any of: on the play button, on the timeline and as a small black-and-white logo.
- **When the app plays nothing**: **Player** shows the app in the same style, with the volume; **Classic** is the original look with icon, title and volume bar (where the logo as background and the bar colour can also be chosen). You can set the volume before the app makes a sound; it applies as soon as it does.
- **App closed**: the dial shows "Open" with the app's name, and a click starts it.
- **Title**: the app name or your own text.

### Game Key (key)

A key with the logo, name and playtime of a game.

- Without a chosen game the key **follows the game that is open**. Choose one game under **Game** and the key belongs to that game and can also **start** it (through Steam, Epic, Lunar Client or its .exe).
- **Click**: start, minimize or show again. **Hold for 1 second**: close the game.
- **Display**: show the name while playing, show the playtime while playing, and for a chosen game show its name while it is closed.
- **Logo**: pick your own image (PNG, JPG or SVG). On a key that follows the open game, the logo applies to the game that is open right now. The plugin keeps a copy in `%APPDATA%\StanGameVolume\logos`, so the logo stays when you move the file. The logo applies to every key and dial of that game.

### App Key (key)

A key with the icon and name of one app.

- **Click**: minimize, show again, or open the app if it is closed.
- **Hold for 1 second**: close the app. If it keeps running in the system tray, like Discord, it is shut down completely after 3 seconds, unless a window is still open (for example a save prompt).
- **Display**: with or without the name.

## Scarlett Switch

Switches a Focusrite Scarlett between speakers and headphones, from a key or a Stream Deck + dial. Requires a Focusrite Scarlett, Focusrite Control 2 and a MIDI loopback port (for example through Windows' MIDI Loopback Setup). The plugin sends the switch command to Focusrite Control 2 through that port.

- **Scarlett Switch** (key or dial): switches between speakers and headphones and shows which one is on. On a dial: press or tap = switch, turn = Windows volume, long touch = mute.
- **Speakers** and **Headphones** (keys): set the Scarlett straight to that device and light up while it is on.

Options:

- **Status**: whether the Scarlett, Focusrite Control 2 and the MIDI port were found. Red means something is missing, with the reason.
- **Look** (dial): **Player**, in the style of the media and Spotify dials with the device name as title, or **Classic** with icon, title and volume bar. With Classic you choose the device or your own text as title.
- **Speakers** / **Headphones** (dial): your own names, such as "Yamaha HS5" or "DT 1990 Pro".
- **Custom icons** (Scarlett Switch): your own icon for speakers and for headphones, from a file or from the icons installed in Stream Deck (**Library**).
- **Speaker mix** / **Headphone mix**: which Focusrite Control 2 mix (A, B or C) belongs to each device.
- **Focusrite**: start Focusrite Control 2 hidden and close it afterwards, start it hidden and keep it running, or never start it.
- **Volume step** (dial): 1, 2, 3, 5 or 10% per step.
- **MIDI**: the name of the loopback port, and **Apply again** to send the settings to Focusrite Control 2 again.

Settings are stored in `%APPDATA%\ScarlettSwitch`.

## Monitor

- **Monitor Input**: switch between the inputs of a monitor with a key. The monitor must support DDC/CI. Click = next input; previous input on double-click, long press or off (**Previous input**).

In the menu, tick only the inputs that have something connected (**Inputs in use**). A monitor does not report which inputs have a signal.

Options:

- **Monitor**: which monitor the key controls, with your own name.
- **Edit input**: per input your own name, the device connected to it (automatic, PC, PlayStation 5 Pro or none) and your own icon from a file or the Library.
- **Style**: **Basic**, **Tile** (bright while the display is connected) or **Icon only**, and whether text is shown on the key.
- **PlayStation**: keep the PC and the other displays awake while the PS5 is on screen, so OBS and Discord keep running, and choose which display becomes the main display meanwhile.
- **Disconnect while not on this PC**: see below.

LG monitors ignore the standard input command. On a PC with an NVIDIA graphics card the plugin therefore sends LG's own command through the graphics card. An LG monitor does not report which input is active; the key shows the input it last switched to itself. Other brands, such as Alienware, work as usual. The model name comes from the monitor's EDID, because some monitors (including LG) report a wrong model name over DDC/CI.

**Disconnect while not on this PC** (off by default, per key): when the monitor switches to an input other than this PC's, such as a console, Windows removes it from the display layout, so the mouse moves straight to the next display. Switching back to the PC restores the layout. If that ever fails, Windows + P → Extend restores everything.

You can choose your own icon per input. The plugin ships a PC and a PS5 icon; they are in the Library under "Monitor".

## Better Spotify Dial

Spotify on a Stream Deck + dial, with the same screen as the official Spotify plugin: cover, title, artist, time, progress, shuffle, play and repeat. The difference: you choose what the dial does.

- **Click 1x, 2x and 3x, hold and tap the screen**: per gesture choose play/pause, next or previous track, volume, mute, shuffle, repeat, show the Spotify window or open a playlist. Default: click = play/pause, double-click = next track, triple-click = previous track, hold = show or hide Spotify.
- **Turn**: Spotify volume or previous/next. A pressed turn seeks 5 seconds per step, or does the other one.
- **Styles**: Player (like the official dial), Picture (the cover across the whole screen) or Compact. Spotify green on the play button, the timeline and/or as a small logo (each can be ticked) makes the dial recognisable next to other media dials.
- **Long touch on the screen**: left = previous track, right = next track.
- If Spotify is closed, every action starts Spotify.

Works with the Spotify app for Windows, without a Spotify account or sign-in in the plugin: everything goes through Windows' media controls. If Elgato's official Spotify plugin is also installed, the dial uses its icons, so it looks exactly the same.

## PlayStation 5 for OBS

For anyone who hears a PlayStation 5 in OBS through a capture card with pass-through.

- **PS5 Volume** (dial): turn to set how loud you hear the PlayStation, pressed turn in 1% steps. Press or tap toggles mute, or starts OBS if it is closed. Hold sets the volume back to your default.
- The dial looks like the Spotify dial: the name with **On** or **Off** below it, a speaker icon bottom left (crossed out when muted), the percentage bottom right and a PlayStation-blue bar. A small PlayStation logo sits in the corner of the tile. While the PlayStation is off, everything turns grey.
- In the settings you turn the blue bar (otherwise white) and the small logo on or off separately.
- The plugin reads on or off from the capture card's audio. With obs-websocket enabled (Tools > WebSocket Server Settings) it also looks at the picture and recognises the card's "NO SIGNAL" screen. The plugin reads the password itself from the OBS settings on this PC.

The volume is OBS in the Windows volume mixer: it only changes what you hear, not your stream or recording. The sources are called "PS5 beeld" and "PS5 geluid" by default; choose other names in the dial's settings. The audio source is also found when it is not in the active OBS scene collection.

## F1 Race Control

For watching F1 with [MultiViewer](https://multiviewer.app/download) (required) and [Ultimate F1 Viewer](https://github.com/MRAJEKO/UF1-Viewer-with-F1MV/releases/latest) (optional). If either is missing, the plugin says so and offers the download page.

- **F1 Mode**: sets everything up on a separate "F1" desktop. Double-click hides and mutes everything, hold for 1 second closes everything.
- **Race** / **Quali**: opens your MultiViewer setup and Ultimate F1 Viewer layout with that name, for the live session or otherwise the latest replay. Fills onboards of drivers outside the top teams with the best of the rest and picks the drivers in battle mode.
- **Session**: time or laps remaining, and the weather. Without a running session it shows the next session with a countdown.
- **Pause**, **Go live**, **Fullscreen**: MultiViewer controls.
- **Sync**: press to sync all players with the main screen (as an F1 Party guest: to the host first). Hold 1 second to host an F1 Party and post its invite to Discord; when you already host one, the invite is posted again (set the webhook in F1 Party).
- **F1 Party**: when you join a party in [F1 Party](https://github.com/Jeremy0538/f1party) with "Stream Deck: F1 Race Control" chosen, the plugin opens the host's session as Race, Quali or Practice would. It listens on `127.0.0.1:9335` only (`GET /f1/status`, `POST /f1/open {"kind": "race", "contentId": "…"}`) and refuses requests from web pages.
- Dials: **Live delay**, **Volume**, **Track status** (flags on Philips Hue) and **Flag lights** (which Hue room).

Philips Hue is optional: pair your bridge in the settings of the Track status dial. Without Hue those functions show "Not set up". Use your own logo locally: put it in `%APPDATA%\StanF1` as `f1-logo.svg` or `f1-logo.png`. F1 Mode shows it on its key, and `install.ps1` also puts it on the plugin, category and F1 Mode icons of the installed copy. No logo is shipped with the plugin.

**Privacy:** the plugin stores no F1 TV or MultiViewer sign-in and sends no data to its author. MultiViewer and Ultimate F1 Viewer use their own sign-in on your own PC. Settings, such as your Hue Bridge key and window positions, are only stored locally in `%APPDATA%\StanF1`. For replays the plugin requests public data from OpenF1 and F1 TV, without signing in. MultiViewer and Ultimate F1 Viewer are started with a debug port (9334 and 9333) that can only be reached from your own PC.

Works on Windows with the Stream Deck app or with [OpenDeck](https://github.com/nekename/OpenDeck), and on Linux (x64) with OpenDeck. The `F1.streamDeckPlugin` file contains both versions; install it in OpenDeck through the Plugins tab. Only one app can drive a Stream Deck at a time, so on Windows close the Stream Deck app completely before you start OpenDeck. Building from source: `F1\install.ps1 -OpenDeck` installs into OpenDeck instead of Stream Deck.

| | Windows (Stream Deck or OpenDeck) | Linux (OpenDeck) |
|---|---|---|
| Flags on Hue, latency, volume, pause, live, sync, fullscreen, Session | yes | yes |
| Race / Quali: MultiViewer setups, Ultimate F1 Viewer layouts, best of the rest, battle mode | yes | yes |
| Find the latest replay (OpenF1, F1 TV) and open it in MultiViewer | yes | yes |
| Start (with debug port) and close MultiViewer and Ultimate F1 Viewer | yes | yes |
| Separate "F1" desktop | yes | no, you stay on the current workspace |
| Put windows in place, stack by height, battle mode in front of the flag bar | yes | no, your window manager does that |
| Double-click F1 Mode: minimize everything | yes | no (muting does work) |
| Message when an app is missing | window with Yes/No | zenity or kdialog, otherwise a notification and the download page through `xdg-open` |

On Linux the plugin finds MultiViewer as .deb/.rpm (`multiviewer`), as Flatpak or as an extracted zip, and Ultimate F1 Viewer as an AppImage in for example `~/Applications` or `~/Downloads`, or through a shortcut in `~/.local/share/applications`. If an app is somewhere else, set its path as `MultiViewerPath` or `Uf1Path` in `~/.config/StanF1/settings.json`. On Linux the settings, the log and a custom logo are in `~/.config/StanF1`. The Stream Deck + dials depend on what OpenDeck and your device support.

## Building

Requires the .NET 10 SDK. `install.ps1` in `VolumeLauncher`, `Scarlett`, `Monitor`, `Spotify`, `PS5` or `F1` builds a plugin and installs it on this PC. Every `install.ps1` then also puts the update on GitHub as a new release, through `publish.ps1` (for `PS5` only with `-Publish`). `package.ps1` only creates the installers in `dist\`.

`Scarlett` uses a few files from `VolumeLauncher\src` (icons, image loading, the log), so both plugins look the same.

### Keeping track of what's new

For every change, add a line to [CHANGELOG.md](CHANGELOG.md) under **Unreleased**, in the group **New**, **Improved** or **Fixed**, with the plugin name first. `publish.ps1` uses those lines as the release notes of the new release and moves them in CHANGELOG.md under the new version number.
