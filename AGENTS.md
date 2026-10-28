# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
its features; this file covers how the code is put together and the rules that keep it working.

## What this is

A recreation of Atari's 1980 vector arcade game Battlezone, in C# on .NET 10 and Avalonia 12. The 3D wireframe
world is projected and drawn with Avalonia's `DrawingContext`. One self-contained executable per platform
(Windows, macOS and Linux, x64 and ARM). The sounds are synthesised by `tools/make_sounds.py`.

This repo is older and simpler than the author's later AVA games: one project at the repo root, no tests, no
`global.json` and no `Directory.Build.props`.

## Commands

```bash
dotnet build AVAZone.slnx                 # needs the .NET 10 SDK
dotnet run --project AVAZone.csproj       # play it; it takes no command-line arguments
python3 tools/make_sounds.py              # rebuild Sounds/ (standard-library Python only)
```

There are no tests yet.

## Layout

- `AVAZone.csproj`, `AVAZone.slnx`, `Program.cs`, `App.axaml`, `MainWindow.axaml`: the app, at the repo root.
- `Engine/`: the game.
  - `GameEngine.cs` runs the states (start screen, attract, play, pause, game over, initials, high scores),
    the tank, shells, the saucer and scoring.
  - `GameEngine.Enemies.cs` spawns and steers the enemy: tanks, then missiles, then supertanks as the score
    climbs.
  - `AttractDriver.cs` drives the tank in attract mode.
  - `InputHandler.cs` (keys, mouse) and `ControllerManager.cs` (SDL2 gamepads) give the two treads and the
    buttons.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `Models/`: `Vec3`, `Camera` (the periscope's view and projection), `Wireframe` (the line models), `Entities`,
  `Explosion` and `Debris`.
- `Views/`: `GameCanvas` (split over partial files) draws the frame scaled to the window and runs the timer.
  `GameCanvas.World.cs` draws the plain, mountains and volcano; `GameCanvas.Screens.cs` the HUD, radar,
  title, initials and high-score screens.
- `Sounds/`: the WAV files `tools/make_sounds.py` writes, copied next to the executable.
- `Images/`: the README's screenshots.

## Rules that keep it working

- **Timing.** There is no fixed-step loop: a `DispatcherTimer` ticks about every 16 ms and `GameEngine.Update`
  takes the real elapsed time. Speeds and timers are per second, so scale anything new by `deltaTime`.
- **Random numbers** come from one unseeded `Random` in `GameEngine`, so play is not repeatable.
- **Input.** `InputHandler` turns keys, mouse and pad into two tread values from -1 to 1, as the cabinet's two
  sticks did. Forward is positive; a stick's Y is negative when pushed up, so it is negated. Keys are tracked by
  `Key`.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`, so SDL is
  never called from two threads at once.
- **Sound.** `SoundManager` plays the WAV files through `NetCoreAudio`, which uses the system's player. On Linux
  that is `aplay` (from `alsa-utils`); without it the game is silent.
- **High scores** live in the app-data folder (`SpecialFolder.ApplicationData`, `AVAZone`).
- **Line endings.** `Program.cs`, `App.axaml`, `App.axaml.cs`, `MainWindow.axaml.cs` and `app.manifest` are CRLF;
  the rest are LF. Keep each file's existing line endings so diffs show only real changes.

## Making a change

- Build and play-test by hand: there are no tests to catch a regression.
- Update `README.md` (features, controls, project structure) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds every push and pull request (there is no test step). It then publishes
self-contained single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow
artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper
release. A release also asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
