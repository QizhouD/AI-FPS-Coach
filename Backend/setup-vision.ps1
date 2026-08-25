# One-time CUDA vision environment for the GPU gaming PC.
# Do not run Backend/run.ps1 before this script: it installs CPU torch.
param(
    [string]$CudaTag = "",
    [switch]$SkipModel
)

$ErrorActionPreference = "Stop"

$backendRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $backendRoot
Set-Location -LiteralPath $backendRoot

function Get-DriverCudaVersion {
    $smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if (-not $smi) {
        return $null
    }
    $output = & nvidia-smi 2>&1 | Out-String
    if ($output -match "CUDA Version:\s*([\d.]+)") {
        return [version]$Matches[1]
    }
    return $null
}

function Resolve-CudaTag([string]$requested, [version]$driverCuda) {
    if ($requested) {
        return $requested
    }
    if ($null -eq $driverCuda) {
        throw "nvidia-smi not found or CUDA Version missing. Install the NVIDIA driver, or pass -CudaTag cu124."
    }
    if ($driverCuda -ge [version]"12.8") { return "cu128" }
    if ($driverCuda -ge [version]"12.6") { return "cu126" }
    if ($driverCuda -ge [version]"12.4") { return "cu124" }
    if ($driverCuda -ge [version]"12.1") { return "cu121" }
    if ($driverCuda -ge [version]"11.8") { return "cu118" }
    throw "Driver CUDA $driverCuda is too old for current PyTorch wheels. Pass -CudaTag explicitly if you know it works."
}

$driverCuda = Get-DriverCudaVersion
if ($null -eq $driverCuda) {
    Write-Error "nvidia-smi failed. Install an NVIDIA driver before setting up vision."
    exit 1
}

$cudaTag = Resolve-CudaTag $CudaTag $driverCuda
Write-Host "Driver CUDA $driverCuda -> PyTorch index $cudaTag"

if (-not (Test-Path -LiteralPath ".venv")) {
    python -m venv .venv
}

$python = Join-Path $backendRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $python)) {
    throw "Virtualenv python not found at $python"
}

Write-Host "Installing CUDA torch ($cudaTag) before requirements.txt ..."
& $python -m pip install --upgrade pip
& $python -m pip install --upgrade torch torchvision --index-url "https://download.pytorch.org/whl/$cudaTag"
& $python -m pip install -r requirements.txt

$cudaCheck = & $python -c "import torch; print('CUDA', torch.cuda.is_available()); print('DEVICE', torch.cuda.get_device_name(0) if torch.cuda.is_available() else '')" | Out-String
Write-Host $cudaCheck.Trim()
if ($cudaCheck -notmatch "CUDA True") {
    throw "torch.cuda.is_available() is False. Stop here; do not start the API. Try another -CudaTag (cu124/cu121) matching nvidia-smi."
}

$modelsDir = Join-Path $repoRoot "models"
$mediaDir = Join-Path $repoRoot "media"
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null
New-Item -ItemType Directory -Force -Path $mediaDir | Out-Null

$modelPath = Join-Path $modelsDir "yolov8m-csgo.pt"
if (-not $SkipModel -and -not (Test-Path -LiteralPath $modelPath)) {
    Write-Host "Downloading keremberke/yolov8m-csgo-player-detection -> $modelPath"
    & $python (Join-Path $backendRoot "tools\download_enemy_model.py") --output $modelPath
    if (-not (Test-Path -LiteralPath $modelPath)) {
        throw "Model download finished but $modelPath is missing. Download best.pt manually from https://huggingface.co/keremberke/yolov8m-csgo-player-detection"
    }
}

$envFile = Join-Path $repoRoot ".env"
if (-not (Test-Path -LiteralPath $envFile)) {
    @(
        "FPS_VISION_ENEMY_MODEL_PATH=$modelPath"
        "FPS_VISION_CROSSHAIR_MODEL_PATH="
        "FPS_VISION_CROSSHAIR_BASELINE=true"
        "FPS_VISION_DEVICE=cuda"
        "FPS_VISION_CONFIDENCE=0.25"
        "FPS_VISION_MEDIA_ROOT=$mediaDir"
    ) | Set-Content -LiteralPath $envFile -Encoding utf8
    Write-Host "Wrote $envFile"
} else {
    Write-Host "Keeping existing $envFile"
}

Write-Host "Setup complete. Start the API with: .\Backend\run-vision.ps1"
Write-Host "Point OBS output at $mediaDir or set FPS_VISION_MEDIA_ROOT in .env to the OBS folder."
