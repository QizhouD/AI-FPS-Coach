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

## Current milestone (P0)

Get one practice-range recording through local CUDA inference with boxes
aligned. Detection rate is not the goal yet.

- Video jobs no longer fail on missing opencv or missing weights
- A `completed` job returns detections including `part=head`
- `recommended_aim.target_id` points at the enemy head nearest screen center
- Boxes line up with enemies on the tactical screen, and markers follow pause and
  seek
- Record where the CSGO model misses on CS2 footage; log it, do not swap models
  during P0

Out of scope for P0: shot detection, pixel-to-angle conversion, job pagination,
crosshair training, LAN split, and video upload endpoints.

## Constraints when changing code

- The video endpoint takes a **local path** that must resolve inside
  `FPS_VISION_MEDIA_ROOT`. Do not turn it into a cross-machine upload.
- `FPS_VISION_DEVICE` reaches `YOLO.predict(device=...)`. Use `cuda` when a GPU
  is present; do not hard-code `cpu`.
- `/health` must keep reporting CUDA and model readiness.
- Enemy labels pass through `normalize_label()`: `ct*` becomes CT, `t*` becomes
  T, and any label containing `head` becomes the head part. Class names must line
  up when swapping weights.
- Starting weights are `keremberke/yolov8m-csgo-player-detection`, whose classes
  (`ct / cthead / t / thead`) match `normalize_label()` as-is and load cleanly on
  ultralytics 8.4. It is a CSGO model, so its accuracy on CS2 footage still needs
  measuring. The jparedesDS CS2 models are gated and do not block P0.
