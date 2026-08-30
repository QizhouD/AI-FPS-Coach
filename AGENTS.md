# Agent Onboarding

This is a single-machine product: Unity, OBS, FastAPI and CUDA YOLO all run on
one Windows PC that both plays CS2 and has an NVIDIA GPU. Practice is only
recorded while playing; analysis runs after recording stops, so the game and
inference never contend for the GPU.

Detailed Chinese design notes and runbooks live under `doc/`, which is
deliberately untracked. If `doc/` is missing in your clone, the setup steps below
are still complete on their own.

## Read before installing anything

1. Run `nvidia-smi` first. If it fails, install the NVIDIA driver and stop here.
2. For a first install run **only** `.\Backend\setup-vision.ps1`. Do not run
   `.\Backend\run.ps1` first: it installs **CPU** torch into `.venv`, which is
   painful to undo.
3. Do not copy `.venv` from another machine.
4. Never commit `.pt` weights, recordings, or `.dem` files.
5. Keep uvicorn and Unity on `127.0.0.1`. This is not a client/server split.
6. A crosshair model is optional: `FPS_VISION_CROSSHAIR_BASELINE=true` uses the
   screen center, which is correct for a first-person view.

## One-time setup

Requirements: Windows 10/11, Python 3.10 or 3.11, Unity `6000.2.15f1`, OBS, and a
working NVIDIA driver.

```powershell
nvidia-smi
.\Backend\setup-vision.ps1
```

The script creates `Backend\.venv`, installs **CUDA torch before**
`requirements.txt`, downloads `yolov8m-csgo` into `models\`, and writes a
gitignored `.env`.

It must finish with `OK gpu compute verified`. That check runs a real matmul on
the GPU instead of trusting `torch.cuda.is_available()`, which can report success
on a wheel that then fails at kernel launch.

The CUDA tag is inferred from the driver, or pass it explicitly:

```powershell
.\Backend\setup-vision.ps1 -CudaTag cu128
```

**RTX 50 series (Blackwell, `sm_120`) needs cu128 or newer.** Older wheels
install fine but cannot launch kernels on it. `tools/check_cuda.py` prints the
device capability alongside the wheel's arch list so a mismatch is obvious.

If PowerShell blocks the script:

```powershell
powershell -ExecutionPolicy Bypass -File .\Backend\setup-vision.ps1
```

## Daily start

```powershell
.\Backend\run-vision.ps1
```

Serves `http://127.0.0.1:8000`; leave the window open. Verify with:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
```

Expect `vision.cuda_available` true and `vision.enemy_model` `ready`.

Unity: open `UnityClient`, load `Assets/Scenes/Main.unity`, press Play. The
endpoints already point at localhost.

OBS: 1080p, 60 fps, with an audio track. The output directory must sit inside
`FPS_VISION_MEDIA_ROOT` (defaults to `media\`, changeable in `.env`). Otherwise
`POST /api/v1/vision/video` returns 400 and Unity falls back to per-frame JPEG.
The audio track is not optional if you want shot metrics: OpenCV discards audio,
so firing moments come from a separate ffmpeg pass over the same file.

Set `FPS_VISION_FOV_DEG` to the FOV the footage was recorded at. Deviations are
reported in degrees, and the conversion is wrong at the wrong FOV.

## Driving the Editor from the command line

`com.unity.pipeline` is in the manifest, so a running Editor exposes itself on a
local port and the Unity CLI can drive it. This is how P0 was verified, and it
beats batch mode here: `VideoPlayer` needs a real render loop, so the check has
to run in a live Editor rather than headless.

```powershell
unity status                       # port, project, version, play state
unity list                         # every command the Editor exposes
unity command set_autotick --enable true    # keep ticking while unfocused
unity command editor_play
unity command eval_file --file reports\p0\eval\01_probe.cs
unity command capture_game_view --source camera --width 1920 --height 1080 `
  --save_path "Screenshots/shot.png"
```

Three things that will otherwise cost you an hour:

- Arguments are `--name value`. Passing `name=value` fails with a hint that
 reads like the parameter does not exist.
- Use `eval_file`, not `eval`. Any C# with quotes in it gets shredded by the
 CLI's argument splitter. Keep the snippets outside `Assets/` or the Editor
 will try to compile them as project scripts.
- `capture_game_view` renders at the Game view's aspect and then stretches the
 result into the size you asked for, so a 2.8:1 Game view squashed into
 1920x1080 comes out horizontally compressed. `--source camera` renders at the
 requested size instead; use it whenever the image is evidence.

`save_path` is relative to `Assets/`. Move captures out afterwards, or Unity
imports every one of them as a texture.

## Current milestone

The measurement layer is built, tested, and now validated on real footage.

Done: pixel-to-angle conversion, session-level aim metrics, JSON session
persistence, audio-based shot detection, progressive job results, and a
demo-to-video pairing tool that produces ground truth without hand labelling.

Measured on 88 seconds of Aim Botz at 1080p60, sampled at 10 fps:

| | imgsz 640 | native 1920 |
| --- | --- | --- |
| head found on a boxed player | 14.4% | 65.6% |
| median crosshair deviation | 38.4 deg | 1.5 deg |
| within 5 deg of a head | 6.5% | 72.8% |
| shots aligned to a frame | 78/105 | 105/105 |
| median deviation at the shot | 36.1 deg | 0.8 deg |

So the starting CSGO weights are adequate on CS2 footage, and the apparent
failure was the inference resolution. Fine-tuning and swapping models are both
off the critical path until a measurement says otherwise.

Time alignment between a recording and its demo is no longer a risk. On a
genuinely simultaneous pair, audio onsets found 56 of the demo's 57 fire events
and `estimate_time_offset` matched 89% of them, with a residual of 5.7 ms
standard deviation and no bias, which is a third of a frame at 60 fps. Every
matched pair fell inside one frame. Full numbers in
`reports/experiments/imgsz-experiment-*.md`.

The Unity client passed P0 on 2026-08-30: overlay boxes register with the
footage, seeking lands on the right result including backward and repeated
jumps, nothing drifts while paused, and the metrics rail reads from the live
session. Getting there took two client fixes, both described in
`reports/p0/report.md` along with the numbers.

Both defects were invisible from the service side, which is the lesson worth
carrying. The footage was rendered mirrored while the overlay was not, so every
box sat on the opposite side of the frame from its target, and the service
reported the same deviation either way. Separately the metrics rail was never
wired to the overlay, and the null reference failed silently rather than
logging. Check the client renders what the numbers claim; the numbers alone
cannot tell you.

Shot metrics need `sample_rate` at 8 fps or more. `MAX_ALIGNMENT_SECONDS` is
0.12 s, so below that most shots land between samples and silently go unaligned,
and overcorrection cannot be detected at all because its 0.35 s window holds
fewer than two samples. Both conditions say so in `ShotStats.message` rather
than reporting a low number as if it were a result. The Unity client asks for
10 fps for this reason; at that rate all 56 shots in the range recording
aligned.

Reaction time still needs targets that appear and disappear. On an aim trainer
map the bots never leave the screen, so the whole recording is one engagement
and `reaction_samples` comes back as 1. Both the service message and the client
card say so instead of quoting the figure.
Ground truth no longer needs coordinates, a GOTV demo, or hand labelling. A
registered headshot kill is a label in itself: the server confirmed the bullet
passed through a head hitbox, so at that tick the crosshair was on a head to
within the head's own angular radius, 0.25 deg at typical range. `player_death`
with `hitgroup` and `tick` parses cleanly out of a plain `record` POV demo. Use
`record <name>` then `stop`; do not bother with `tv_record`, which will not start
on a workshop practice map.

That method immediately caught an error every proxy metric had missed, described
under target selection below.

Do not expect entity positions from a POV demo. The data is there, since
`list_updated_fields` reports `CCSPlayerPawn.m_vecX/Y/Z` and `m_angEyeAngles`
among 267 fields, but demoparser2 cannot surface it: `parse_ticks` returns zero
rows for every prop name on both 0.41.4 and 0.42.0, `parse_player_info` returns
zero rows, and event coordinate columns come back all-null. This is upstream
(LaihoE/demoparser issues 272, 321, 329), not a mistake in this repo.

Only once that number exists does fine-tuning versus swapping weights become a
decision rather than a guess. `tools/compare_accuracy.py` runs that comparison;
the older `tools/compare_models.py` is a different thing, a PyTorch-versus-ONNX
check on still images.

Two risks in the pairing are still unverified: whether range bots carry full
position data in the demo, and how precisely video frames align to demo ticks.
The tool estimates the time offset from gunshots and reports its confidence, so
check that score before quoting any figure. Test with a very short clip first.

Out of scope: LLM coaching, a database, Valorant, crosshair training, a LAN
split, video upload endpoints, and a 2D or 3D demo replay view.

## Constraints when changing code

- The video endpoint takes a **local path** that must resolve inside
 `FPS_VISION_MEDIA_ROOT`. Do not turn it into a cross-machine upload.
- Aim offsets are `None`, never `0.0`, when no head is visible. Collapsing the
 two makes "nothing on screen" indistinguishable from "perfectly on target" and
 drags every aggregate toward zero.
- Statistics read the raw detection; only the overlay marker sees the smoothed
 point. Measuring the smoothed point reports the filter, not the player.
- **A body box counts as a target.** At range the detector boxes a player but
 not their head, so ranking only detected head boxes picked a nearer bot far
 off-axis and reported its angle instead. Measured against 44 server-confirmed
 headshots, that put the median error at the moment of firing at 2.48 deg with a
 third of shots above 5 deg, from a truth of 0.25. Placing the head
 `HEAD_FRACTION_OF_BODY` down from the top of the body box brings it to 0.36 deg
 with 7% above 5. Do not narrow selection back to head boxes only, and keep
 reading `target_source` before quoting a figure as a head measurement.
- Anything feeding a reported number belongs in a tested module, not in a CLI.
 `geometry`, `metrics`, `projection`, `evaluation` and `shot_detector` are all
 pure and all covered.
- `FPS_VISION_DEVICE` reaches `YOLO.predict(device=...)`. Use `cuda` when a GPU
 is present; do not hard-code `cpu`.
- **Never let inference run at ultralytics' default 640 on 1080p footage.**
 `image_size_for()` defaults to the frame's native long side for a reason: a
 head at range is around 10 px in the source frame, and letterboxing to 640
 leaves roughly 3 px, under the 8 px stride of the finest feature map. Measured
 on range footage, 640 found a head on 15% of already-boxed players against 66%
 at native 1920, and cost slightly *more* wall time, because the bottleneck is
 video decode rather than the network. This one setting moved the median
 measured crosshair deviation from 38.4 deg to 1.5 deg on the same recording.
- `/health` must keep reporting CUDA and model readiness.
- Enemy labels pass through `normalize_label()`: `ct*` becomes CT, `t*` becomes
  T, and any label containing `head` becomes the head part. Class names must line
  up when swapping weights.
- Starting weights are `keremberke/yolov8m-csgo-player-detection`, whose classes
  (`ct / cthead / t / thead`) match `normalize_label()` as-is and load cleanly on
  ultralytics 8.4. It is a CSGO model, so its accuracy on CS2 footage still needs
  measuring. The jparedesDS CS2 models are gated and do not block P0.
