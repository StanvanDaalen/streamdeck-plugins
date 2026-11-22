# Build your F1 setup with an AI assistant

Copy everything below the line into an AI assistant that can **see your screen and run commands on your
computer** (for example Claude Code with computer use). A chat-only assistant can't move windows; it can only
walk you through the steps.

The prompt builds one MultiViewer setup and one Ultimate F1 Viewer layout per session type (race,
qualifying, practice), fitted to your own monitors, and checks every pixel with a bright yellow desktop.
It backs up what you have first and never signs in to anything.

---

You are going to build my Formula 1 viewing setup: MultiViewer (MV) setups and, if I have it, Ultimate F1
Viewer (UF1) layouts, one per session type, fitted exactly to my monitors. Work step by step, show me what
you plan before you change anything, and ask me when a choice is mine to make.

## Rules

- **Back up first.** Before you change anything, copy my MV setups and my UF1 config to a dated backup folder
  and tell me where it is. Never delete or overwrite an existing setup or layout without asking; new ones get
  new names unless I say otherwise.
- **Look at the screen, not at window data.** Window borders, shadows and DPI scaling make reported sizes
  lie by a few pixels. What counts is what a screenshot shows.
- **Put my desktop background back** when you are done, also if something fails halfway.
- Don't sign in, don't touch passwords or F1 TV accounts, and only move MV and UF1 windows.
- Keep me informed in short lines: what you are doing now, what you found.

## 1. Find out what I have

1. My monitors: how many, resolution, scaling (DPI), and how they are placed relative to each other
   (Windows: display settings; read them with a command rather than guessing). Which one is the main
   monitor (where the taskbar and the main F1 stream go)?
2. Is MultiViewer installed and running? It has to run **with its debug port** for setups to be saved
   from outside: close it and start it with `--remote-debugging-port=9334` (Windows:
   `%LOCALAPPDATA%\multiviewer\app-<version>\MultiViewer.exe`). Its local API is
   `http://localhost:10101/api/graphql`; its windows are reachable at `http://127.0.0.1:9334/json/list`.
3. Is Ultimate F1 Viewer installed? (Windows: `%LOCALAPPDATA%\Programs\ultimate-f1viewer-with-f1mv` or
   `Program Files\Ultimate-F1Viewer-With-F1MV`.) Start it with `--remote-debugging-port=9333` so its
   layouts can be saved from outside. Its layouts live in its `config.json` under `layouts`.
4. My existing MV setups: in any MV window, `await window.f1mv.setupManager.getSetups()` returns them
   (each has `name`, `windows[]` with `type`, `state` and `windowState.bounds`). List them for me.
5. Open a session in MV to work with (any replay is fine; the layout doesn't depend on it).

## 2. Ask me a few things

- Which monitors may F1 use, and which one is the main monitor?
- Which session types do I want: race, qualifying, practice (sprint weekends use the same as race and
  qualifying)?
- Which onboards do I want for sure (for example the drivers of the top teams), and do I want two
  "best of the rest" onboards for the best drivers outside them?
- Do I use the F1 Race Control Stream Deck plugin? (Then name the setups so its keys find them; see step 7.)

## 3. Layout principles (a proven layout to start from)

These come from a setup that has been used and refined over many race weekends. Use them as the default,
then adapt to my monitors and answers.

**General**
- **No gaps and no overlap you can see.** Windows sit edge to edge. Where two windows meet, the lower one
  may overlap the upper one by a few pixels so its title bar (with the close button) disappears behind it;
  on each monitor, the window placed **higher** on the screen goes **in front**.
- **The whole stream stays visible**: keep the 16:9 aspect ratio (`maintainAspectRatio`), never crop to the
  frame. Fill leftover space with timing windows instead of stretching video.
- **Nothing crosses a monitor edge** and nothing hides under the taskbar. Keep the taskbar on the main
  monitor only, so the other monitors have their full height.
- **Only the main F1 LIVE stream has sound**; every other player is muted.

**Main monitor**
- F1 LIVE with commentary, as the main picture. A nice trick: give the F1 LIVE window a small "restore"
  size in a corner next to the MV and UF1 app windows and make it fullscreen. Double-clicking the stream then
  shrinks it into that corner, so you can reach the apps, and double-clicking again makes it big.

**Onboard monitor** (usually above or next to the main one)
- Four onboards in the four corners, the rest of the screen in the middle used for timing: for example the
  driver tracker (track map) in the middle, touching the onboards on its left and right, with sector times or
  the leaderboard directly above it. Nothing may hang below the bottom edge.
- Two "best of the rest" slots for the best drivers outside the top teams, if I want them.

**Side monitors** (if there are more)
- Timing on the left and right, sized so rows are readable and no half row shows at the bottom
  (show the top 10 completely rather than 10 and a half).
- UF1's flag display works well as a full background on an outer monitor; UF1's battle mode goes **in
  front of** the flag display and may overlap onboards (it has no background of its own).

**Per session type**
- **Race**: race trace (compare with the race leader or a favourite driver, not with a random car), race
  control messages, track limits and investigations next to each other at the **same height**, tyre
  stints, the driver tracker; UF1 battle mode (top 4 of the race on one side, championship top 3 on the
  other) and the flag display.
- **Qualifying**: sector leaderboard, lap time series, speed trap leaderboard, the driver tracker; UF1
  "current push laps" and crash detection. Sector leaderboard in front of race control messages.
- **Practice**: calmer: leaderboard, tyre stints, lap times; fewer onboards are fine.

MV window types you can use in a setup: `f1tv.player` (a stream; `state.streamData` says which, an onboard
also has `state.driverData`) and live timing windows such as `f1.live-timing.index`,
`f1.live-timing.race-trace`, `f1.live-timing.race-control`, `f1.live-timing.track-limits`,
`f1.live-timing.investigations`, `f1.live-timing.tyre-stints`, `f1.live-timing.sectors-leaderboard`,
`f1.live-timing.lap-time-series`, `f1.live-timing.speedtrap-leaderboard`. Read the full list from my
own setups or from MV's live timing menu; MV updates add and rename windows.

## 4. Propose before you build

For each session type, show me a simple sketch per monitor (a text grid with sizes in pixels is fine): which
window goes where. Wait for my OK or changes.

## 5. Build it

**MultiViewer**
1. Open the session's streams and timing windows (MV's own menus, or `playerCreate` on the GraphQL API:
   `playerCreate(input: { contentId, streamTitle: "F1 LIVE" | driverNumber: N, bounds: {x, y, width,
   height}, maintainAspectRatio: true })`).
2. Place every window at its spot. MV players: `playerSetBounds`. Other windows: move them with the
   operating system (Windows: `SetWindowPos`). Mind DPI: MV's bounds are in Windows "logical" pixels.
3. Save it as a setup in MV (MV's own "save setup", or add it to the list from `getSetups()` and write it
   back with `window.f1mv.setupManager.writeSetups(...)`, keeping all existing setups). Then open it
   again from MV (`restoreSetup`) to make sure it comes back the same.

**Ultimate F1 Viewer** (if installed)
1. Open the UF1 windows for this session type from UF1 itself and place them the same way.
2. Battle mode and crash detection open in "move mode": press Escape in them to lock them in place.
3. Save the layout with UF1's own SAVE (in its main window, through the debug port: `saveLayout(<id>)`),
   with the same name as the MV setup.

## 6. Check it with a yellow desktop

1. Note my current desktop background, then make it **solid bright yellow** (`#FFFF00`) on every monitor.
2. Open the setup (and the UF1 layout on top), wait until all windows have loaded, and take a screenshot of
   every monitor.
3. Every yellow pixel is a gap or a window in the wrong place. Fix them one by one (move, resize, or let the
   lower window overlap a few pixels), then take new screenshots. Only the rounded corners of windows may
   still show a little yellow.
4. Also check: no window crosses a monitor edge, every stream shows its whole picture, no half rows in
   timing windows, the higher window is in front wherever two overlap.
5. Save the setup/layout again with the final positions.
6. **Put my original background back.**

## 7. Finish

- Give every setup a clear name per session type that contains the type: "Race", "Qualifying",
  "Practice" (for example "Race Pit Wall"). The F1 Race Control Stream Deck plugin finds them by these
  words ("Automatic" in its key settings): the Race and Quali keys open the right setup and UF1 layout in one
  press, and a live practice opens with the practice one (from F1 Mode or the Session key).
- Tell me: the setups and layouts you made, where the backup is, and anything that didn't fit perfectly.
- Remind me: after an MV update, live timing windows sometimes change size (for example headers that
  disappear); run the yellow check again then.
