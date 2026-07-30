# FPS AI Coach MVP

The current development focus is offline CS2 demo analysis. The live coaching
workflow remains available as a secondary workspace in the Unity client.

## Requirements

- Unity 6.2 (`6000.2.15f1`)
- Python 3.8 or newer
- Windows 10 or Windows 11

## Start Demo Analysis

Start the local analysis service:

```powershell
.\Backend\run.ps1
```

Then:

1. Open `UnityClient` with Unity `6000.2.15f1`.
2. Open `Assets/Scenes/Main.unity` and enter Play Mode.
3. Keep the `Demo Analysis` workspace selected.
4. Select `Load Sample Report` to verify the UI and API pipeline.
5. Select a real CS2 `.dem` file and optionally enter a player name.
6. Select `Analyze Demo`.

Demo API:

```text
POST http://127.0.0.1:8000/api/v1/analyze/demo
```

The MVP extracts:

- Kills, deaths, assists, and K/D
- Headshots and headshot percentage
- Total damage and ADR
- Opening kills and opening deaths
- Evidence-based training recommendations

Uploaded demos are parsed from a temporary file that is removed after the
request completes. The Unity client currently limits demo files to 512 MB to
avoid excessive memory usage during multipart upload.

## Build the Windows Client

The project uses the Mono scripting backend so that Windows builds do not
require the optional IL2CPP module.

Run:

```powershell
.\build-windows.ps1
```

The output is:

```text
Builds/Windows/FPS-AI-Coach-Live.exe
```

The same build is available from `FPS AI Coach > Build Windows MVP` in Unity.

## Retained Live Workflow

Select `Live Mode` in the Unity header:

1. Add Game Capture to an OBS scene.
2. Start OBS Virtual Camera.
3. Refresh devices in Unity and select `OBS Virtual Camera`.
4. Select `Start Source`.

Local Demo mode produces a lightweight visual pacing tip every 2.5 seconds
without the backend. Disable Local Demo mode to use:

```text
POST http://127.0.0.1:8000/api/v1/analyze/frame
```

For stream output, capture the Unity application window in a separate OBS
output scene. Keep the virtual camera source scene game-only to avoid a video
feedback loop.

## Current Scope

- Unity 6 client
- CS2 demo import and analysis report
- Sample report for pipeline verification
- OBS Virtual Camera and standard video-device input
- Live preview and sampled-frame analysis
- Local FastAPI analysis contracts
- Aim, positioning, decision, and action recommendations
- No persistent frame or demo storage

## Next Analysis Milestones

1. Extract economy, utility, trade kills, and clutch state per round.
2. Calculate first-death position, trade windows, survival time, and fight distance.
3. Generate verifiable conclusions in the rules layer.
4. Use an LLM only for summarization and training-plan generation.
5. Reuse the demo event model in the retained live event pipeline.
