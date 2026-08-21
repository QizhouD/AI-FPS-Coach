# Vision Model Workflow

The runtime API lives under `app/vision`. The first deployment uses the
Ultralytics runtime so that the PyTorch and ONNX baselines can be compared
before moving a stable model into Unity Inference Engine.

## Environment

Set these variables before starting the backend:

```powershell
$env:FPS_VISION_ENEMY_MODEL_PATH = "D:\models\cs2-yolov10s.pt"
$env:FPS_VISION_CROSSHAIR_MODEL_PATH = "D:\models\crosshair\weights\best.pt"
$env:FPS_VISION_CROSSHAIR_BASELINE = "true"
$env:FPS_VISION_DEVICE = "cuda:0"
$env:FPS_VISION_MEDIA_ROOT = "D:\desktop\DQZ\AI-Coach-For-FPS\media"
```

The video job endpoint only accepts files below `FPS_VISION_MEDIA_ROOT`.
When no crosshair model is configured, the default screen-center baseline
returns `(x=0.5, y=0.5)` so recommended aim selection remains usable. Set
`FPS_VISION_CROSSHAIR_BASELINE=false` to require a trained detector.

## Crosshair dataset

Create a single-class Ultralytics dataset with class `crosshair`. Sample
recordings at 2-5 FPS, label the visible crosshair, and split by match rather
than by adjacent frames. Keep source videos outside the Git repository.

Example dataset YAML:

```yaml
path: D:/datasets/cs2-crosshair
train: images/train
val: images/val
test: images/test
names:
  0: crosshair
```

Sample a recording before labeling it:

```powershell
python tools/sample_video_frames.py `
  --input D:/recordings/match.mp4 `
  --output D:/datasets/cs2-crosshair/raw `
  --sample-rate 5
```

Train a small detector first, then export it to ONNX:

```powershell
python tools/train_crosshair.py --data D:/datasets/cs2-crosshair/data.yaml
python tools/export_onnx.py --weights runs/crosshair/weights/best.pt
```

Compare the PyTorch and ONNX predictions on a held-out image directory:

```powershell
python tools/compare_models.py `
  --pytorch runs/crosshair/weights/best.pt `
  --onnx runs/crosshair/weights/best.onnx `
  --image-dir D:/datasets/cs2-crosshair/images/test
```
