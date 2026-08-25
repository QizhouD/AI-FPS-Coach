# AI Coach 视觉模块下一步操作指南

**GPU 游戏机（最终运行机）请先读仓库根目录 `AGENTS.md`，并用 `setup-vision.ps1` /
`run-vision.ps1`。** 产品顺序以 `PRACTICE_REVIEW_DESIGN_ZH.md` 为准。本文第 3–5 节
准心标注与训练在「训练后复盘」形态下 **不是 P0**。

本文档针对当前 Unity 6 + FastAPI 视觉模块，按实际执行顺序说明下一步操作。

## 0. 先确认输入类型

当前项目有两条独立流程：

- `.dem`：CS2 比赛数据解析，用于回合、击杀、位置等统计。
- `.mp4`、`.mkv`、`.avi`：视频视觉推理，用于敌人检测、准心检测和推荐瞄准点。

视觉接口不能直接读取 `.dem`。需要先准备一段 CS2 录屏视频。

推荐目录：

```text
D:\desktop\DQZ\AI-Coach-For-FPS\media\match.mp4
D:\desktop\DQZ\AI-Coach-For-FPS\models\cs2-yolov10s.pt
D:\desktop\DQZ\AI-Coach-For-FPS\models\crosshair\weights\best.pt
```

不要把模型权重或视频提交到 Git 仓库。

## 1. 安装 Python 视觉依赖

**有 NVIDIA GPU 的运行机不要用本节的 `run.ps1` 做首次安装。** 改走仓库根目录
`AGENTS.md`：`.\Backend\setup-vision.ps1`。

打开 PowerShell，进入项目目录：

```powershell
cd D:\desktop\DQZ\AI-Coach-For-FPS
```

首次运行后端脚本会创建虚拟环境并安装依赖：

```powershell
.\Backend\run.ps1
```

脚本启动服务后不要关闭该 PowerShell 窗口。默认地址是：

```text
http://127.0.0.1:8000
```

检查健康状态：

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
```

应该返回：

```json
{"status":"ok","service":"fps-ai-coach-live"}
```

如果只想安装依赖、不启动服务：

```powershell
cd D:\desktop\DQZ\AI-Coach-For-FPS\Backend
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

## 2. 准备敌人检测模型

从模型页面获取 `cs2-yolov10s.pt`：

<https://huggingface.co/jparedesDS/cs2-yolov10s>

如果页面要求接受访问条件，先登录 Hugging Face 并接受条件，再下载模型文件。

把文件放到：

```text
D:\desktop\DQZ\AI-Coach-For-FPS\models\cs2-yolov10s.pt
```

该模型需要包含以下类别或等价命名：

```text
CT
CT_head
T
T_head
```

模型文件不存在时，服务仍然可以启动，但敌人列表会为空。

## 3. 准备准心训练图片

先从录屏中按 5 FPS 抽帧：

```powershell
cd D:\desktop\DQZ\AI-Coach-For-FPS\Backend

.\.venv\Scripts\python.exe tools\sample_video_frames.py `
  --input D:\desktop\DQZ\AI-Coach-For-FPS\media\match.mp4 `
  --output D:\datasets\cs2-crosshair\raw `
  --sample-rate 5
```

然后使用 CVAT、Label Studio 或 Roboflow 标注准心框。

要求：

- 只建立一个类别：`crosshair`。
- 每张图片标注准心的小矩形框。
- 覆盖不同地图、武器、准心颜色、烟雾、闪光和运动模糊。
- 相邻视频帧不要全部放入训练集，应该按比赛或视频拆分训练/验证/测试集。

建议目录：

```text
D:\datasets\cs2-crosshair\
  data.yaml
  images\train
  images\val
  images\test
  labels\train
  labels\val
  labels\test
```

`data.yaml` 示例：

```yaml
path: D:/datasets/cs2-crosshair
train: images/train
val: images/val
test: images/test
names:
  0: crosshair
```

## 4. 训练准心模型

首版使用较高输入分辨率，以保留准心像素：

```powershell
cd D:\desktop\DQZ\AI-Coach-For-FPS\Backend

.\.venv\Scripts\python.exe tools\train_crosshair.py `
  --data D:\datasets\cs2-crosshair\data.yaml `
  --model yolo11n.pt `
  --epochs 80 `
  --imgsz 1280
```

训练完成后确认文件存在：

```text
D:\desktop\DQZ\AI-Coach-For-FPS\Backend\runs\crosshair\weights\best.pt
```

如果有 NVIDIA GPU，可以设置训练设备；如果没有，先使用 CPU 完成小规模验证。

## 5. 导出 ONNX 并比较结果

导出 ONNX：

```powershell
cd D:\desktop\DQZ\AI-Coach-For-FPS\Backend

.\.venv\Scripts\python.exe tools\export_onnx.py `
  --weights runs\crosshair\weights\best.pt `
  --imgsz 1280
```

通常会生成：

```text
runs\crosshair\weights\best.onnx
```

用测试图片比较 PyTorch 和 ONNX：

```powershell
.\.venv\Scripts\python.exe tools\compare_models.py `
  --pytorch runs\crosshair\weights\best.pt `
  --onnx runs\crosshair\weights\best.onnx `
  --image-dir D:\datasets\cs2-crosshair\images\test `
  --imgsz 1280
```

重点观察：

- `mean_iou`：框重合程度。
- `mean_normalized_center_error`：准心中心误差。
- `pytorch_only_detections` 和 `onnx_only_detections`：两种模型的检测数量差异。

如果 ONNX 结果明显异常，先继续使用 `best.pt`，不要立刻迁移到 Unity。

## 6. 配置并启动视觉后端

每次启动服务前设置环境变量：

```powershell
cd D:\desktop\DQZ\AI-Coach-For-FPS

$env:FPS_VISION_ENEMY_MODEL_PATH = "D:\desktop\DQZ\AI-Coach-For-FPS\models\cs2-yolov10s.pt"
$env:FPS_VISION_CROSSHAIR_MODEL_PATH = "D:\desktop\DQZ\AI-Coach-For-FPS\Backend\runs\crosshair\weights\best.pt"
$env:FPS_VISION_CROSSHAIR_BASELINE = "true"
$env:FPS_VISION_MEDIA_ROOT = "D:\desktop\DQZ\AI-Coach-For-FPS\media"
$env:FPS_VISION_DEVICE = "cpu"
$env:FPS_VISION_CONFIDENCE = "0.25"

.\Backend\run.ps1
```

如果暂时没有准心模型，保留 `FPS_VISION_CROSSHAIR_BASELINE=true`。系统会把
屏幕中心 `(0.5, 0.5)` 作为准心位置，并继续计算推荐瞄准点。训练完成后再配置
`FPS_VISION_CROSSHAIR_MODEL_PATH`；如果需要强制模型检测，可以设置
`FPS_VISION_CROSSHAIR_BASELINE=false`。

确认服务启动后，模型状态会通过视觉接口的 `diagnostics` 字段返回。

## 7. 运行 Unity 6 客户端

1. 打开 `UnityClient` Unity 6 项目。
2. 确认后端 PowerShell 窗口仍在运行。
3. 打开 `Assets/Scenes/Main.unity`。
4. 点击 Play。
5. 点击 `IMPORT VIDEO`，选择 `media` 目录中的视频。
6. 等待 VideoPlayer 准备完成并开始播放。

当前 Unity 行为：

- 优先调用 `/api/v1/vision/video`，让 Python 读取本地视频并按 5 FPS 推理。
- 视频路径不在 `FPS_VISION_MEDIA_ROOT` 内时，自动回退到 Unity 上传帧模式。
- 如果后端不可用，视频仍可播放，但不会显示 AI 检测结果。

画面上应该看到：

- 青色：实际准心。
- 橙色框：敌人检测框。
- 黄色标记：推荐瞄准点。
- 连线：实际准心到推荐瞄准点的偏移。
- 左上角文字：推理耗时、置信度、偏移量和目标数量。

## 8. 接口手动验证

验证单帧接口：

```powershell
curl.exe -X POST http://127.0.0.1:8000/api/v1/vision/frame `
  -F "frame=@D:\datasets\cs2-crosshair\images\test\frame_00000001.jpg" `
  -F "timestamp=0" `
  -F "frame_index=0" `
  -F "session_id=manual-test"
```

验证视频路径接口：

```powershell
curl.exe -X POST http://127.0.0.1:8000/api/v1/vision/video `
  -H "Content-Type: application/json" `
  -d "{\"video_path\":\"D:\\\\desktop\\\\DQZ\\\\AI-Coach-For-FPS\\\\media\\\\match.mp4\",\"sample_rate\":5}"
```

接口会返回 `job_id`，然后查询：

```powershell
curl.exe http://127.0.0.1:8000/api/v1/vision/jobs/<job_id>
```

## 9. 验收标准

完成以下项目后，第一阶段即可认为跑通：

- 单帧接口返回 `frame_width` 和 `frame_height`。
- 敌人模型能返回 `part=head` 的检测框。
- 准心模型返回 `actual_crosshair.visible=true`。
- `recommended_aim.target_id` 能指向最近屏幕中心的敌人头部。
- Unity 播放画面显示三类标记和指标文字。
- 视频暂停、播放、跳转后，标记跟随视频时间变化。
- PyTorch 与 ONNX 在测试集上的中心误差可接受。

## 10. 常见问题

### 服务启动但检测为空

检查：

```powershell
Test-Path $env:FPS_VISION_ENEMY_MODEL_PATH
Test-Path $env:FPS_VISION_CROSSHAIR_MODEL_PATH
```

两个命令都应该返回 `True`。同时确认模型路径没有写成目录。

### `/vision/frame` 返回 503

重新安装依赖：

```powershell
cd D:\desktop\DQZ\AI-Coach-For-FPS\Backend
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

### 视频路径接口返回 400

视频必须位于 `FPS_VISION_MEDIA_ROOT` 内。最简单的做法是把视频复制到：

```text
D:\desktop\DQZ\AI-Coach-For-FPS\media
```

### Unity 视频播放但没有叠加层

依次检查：

1. 后端是否能访问 `http://127.0.0.1:8000/health`。
2. Unity Console 是否出现 `Vision frame request failed`。
3. 模型路径是否存在。
4. Unity 中选择的是否是视频文件，而不是 `.dem` 文件。

## 11. 当前阶段不要做的事情

- 不要把 `.dem` 文件直接交给视觉接口。
- 不要把准心和敌人类别混合到同一个新模型中。
- 不要在没有完成 Python/ONNX 对齐前迁移到 Unity Sentis。
- 不要把 `.pt`、`.onnx`、录屏视频和标注数据提交到 Git。

完成本文档第 1–9 步后，再进入 Unity Inference Engine/Sentis 的本地 ONNX 推理迁移阶段。
