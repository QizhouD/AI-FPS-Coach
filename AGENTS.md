# Agent 入门：拉下仓库就能干活

给 **GPU 游戏机**（最终运行机）上的 Cursor Agent。这台机器既能打 CS2，也有 NVIDIA 显卡。整套产品在 **这一台电脑本机跑通**：Unity + OBS + FastAPI + CUDA YOLO。

当前这台若没有 NVIDIA GPU，只改代码、不要跑 `setup-vision.ps1`。P0 验收必须在有独显的那台完成。

产品形态以 `PRACTICE_REVIEW_DESIGN_ZH.md` 为准：训练后复盘，不打实时叠加。游戏过程只录屏，分析在停录之后，因此 CS2 和推理 **不会同时抢 GPU**。

---

## 0. 先读完再动手

1. 先跑 `nvidia-smi`。失败就停，去装 NVIDIA 驱动。
2. **第一次配环境只许** `.\Backend\setup-vision.ps1`。禁止先跑 `.\Backend\run.ps1`：它会把 **CPU 版 torch** 装进 `.venv`，之后 CUDA 很难救。
3. 不要从别的电脑拷 `.venv`。
4. 不要把 `.pt`、录像、`.dem` 提交进 Git。
5. 不要把 uvicorn 改成 `0.0.0.0`，不要把 Unity 的 `127.0.0.1` 改成局域网 IP。这是单机项目。
6. 准心模型不是 P0：`FPS_VISION_CROSSHAIR_BASELINE=true` 用屏幕中心即可。

---

## 1. 一次性环境（clone / pull 之后）

前置：Windows 10/11、Python 3.10 或 3.11、Unity `6000.2.15f1`、OBS、能用的 NVIDIA 驱动。

在仓库根目录：

```powershell
nvidia-smi
.\Backend\setup-vision.ps1
```

若执行策略拦截脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\Backend\setup-vision.ps1
```

脚本会：建 `.venv` → **先装 CUDA 版 torch** → 再装 `requirements.txt` → 下载 `yolov8m-csgo` 到 `models\yolov8m-csgo.pt` → 写根目录 `.env`（已 gitignore）。

成功时终端应出现 `torch.cuda.is_available()=True` 和显卡名。`False` 就先别启动服务。

驱动报 CUDA 12.1 时：

```powershell
.\Backend\setup-vision.ps1 -CudaTag cu121
```

默认 `-CudaTag cu124`。不要把 `ultralytics` 写进「先于 torch」的 pip 命令。

---

## 2. 日常启动

```powershell
.\Backend\run-vision.ps1
```

监听 `http://127.0.0.1:8000`。不要关这个窗口。检查：

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
```

`vision.cuda_available` 应为 `true`，`vision.enemy_model` 应为 `ready`。

Unity：打开 `UnityClient` → `Assets/Scenes/Main.unity` → Play。接口已指向本机，不用改。

OBS：1080p、60fps、带音频。输出目录必须落在 `FPS_VISION_MEDIA_ROOT` 内（默认仓库 `media\`，可在 `.env` 改成 OBS 目录）。否则 `POST /api/v1/vision/video` 会 400，Unity 会退回逐帧 JPEG。

---

## 3. 当前里程碑（P0）

目标：单轮靶场录像在本机 CUDA 上跑完，框对齐。不是检出率，不是开火指标。

验收：

- 视频 job 不再因缺 opencv / 缺权重失败
- `completed` 后结果含 `part=head`
- `recommended_aim.target_id` 指向离屏幕中心最近的敌人头
- 战术大屏框与敌人目视对齐；暂停 / 拖进度条标记跟随
- 记下 CSGO 模型在 CS2 画面上的漏检（只记录，P0 不换模型）

P0 **不要做**：开火检测、角度换算、job 分页、准心训练、局域网拆分、视频上传接口。

P0 通过后再看 `PRACTICE_REVIEW_DESIGN_ZH.md` 第 9 节 P1。

---

## 4. 仓库里什么是权威

| 文件 | 用途 |
| --- | --- |
| 本文件 `AGENTS.md` | 运行机 Agent 的操作说明 |
| `PRACTICE_REVIEW_DESIGN_ZH.md` | 产品形态与实施顺序（决策优先） |
| `VISION_NEXT_STEPS_ZH.md` | 旧视觉清单；第 3–5 节准心训练对 P0 不是必需 |
| `Backend/setup-vision.ps1` | 一次性 CUDA 环境 |
| `Backend/run-vision.ps1` | 日常带视觉启动 |
| `Backend/run.ps1` | 仅 demo 分析；无 CUDA 的机器才用 |

---

## 5. 改代码时的约束

- 视觉视频接口吃 **本机路径**，且必须在 `FPS_VISION_MEDIA_ROOT` 下。不要改成跨机上传，除非产品明确要。
- `FPS_VISION_DEVICE` 已接到 `YOLO.predict(device=...)`。有 GPU 时用 `cuda`，不要写死 `cpu`。
- `/health` 应能看出 CUDA 和模型是否 ready，不要再改回只有 `status=ok`。
- 敌人标签经 `normalize_label()`：`ct*`→CT、`t*`→T、含 `head`→头部。换权重时类别名要对上。
- 起步权重：`keremberke/yolov8m-csgo-player-detection`（CSGO，需在 CS2 画面上实测）。jparedesDS 的 CS2 模型是 gated，不阻塞 P0。
