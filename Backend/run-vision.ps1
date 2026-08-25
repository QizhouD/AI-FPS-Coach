# Daily vision API start for the GPU gaming PC. Loads repo-root .env.
# One-time setup: .\Backend\setup-vision.ps1
param(
    [switch]$Install
)

$ErrorActionPreference = "Stop"

$backendRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $backendRoot
Set-Location -LiteralPath $backendRoot

function Import-DotEnv([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        return
    }
    Get-Content -LiteralPath $path | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith("#")) {
            return
        }
        $eq = $line.IndexOf("=")
        if ($eq -lt 1) {
            return
        }
        $name = $line.Substring(0, $eq).Trim()
        $value = $line.Substring($eq + 1).Trim().Trim("'").Trim('"')
        if (-not $name -or [Environment]::GetEnvironmentVariable($name, "Process")) {
            return
        }
        Set-Item -Path "Env:$name" -Value $value
    }
}

$python = Join-Path $backendRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $python)) {
    throw "Missing $python. Run .\Backend\setup-vision.ps1 first. Do not use run.ps1 on this machine."
}

Import-DotEnv (Join-Path $repoRoot ".env")

$defaultModel = Join-Path $repoRoot "models\yolov8m-csgo.pt"
$defaultMedia = Join-Path $repoRoot "media"
if (-not $env:FPS_VISION_ENEMY_MODEL_PATH) {
    $env:FPS_VISION_ENEMY_MODEL_PATH = $defaultModel
}
if (-not $env:FPS_VISION_CROSSHAIR_BASELINE) {
    $env:FPS_VISION_CROSSHAIR_BASELINE = "true"
}
if (-not $env:FPS_VISION_DEVICE) {
    $env:FPS_VISION_DEVICE = "cuda"
}
if (-not $env:FPS_VISION_CONFIDENCE) {
    $env:FPS_VISION_CONFIDENCE = "0.25"
}
if (-not $env:FPS_VISION_MEDIA_ROOT) {
    $env:FPS_VISION_MEDIA_ROOT = $defaultMedia
}

New-Item -ItemType Directory -Force -Path $env:FPS_VISION_MEDIA_ROOT | Out-Null

if ($env:FPS_VISION_ENEMY_MODEL_PATH -and -not (Test-Path -LiteralPath $env:FPS_VISION_ENEMY_MODEL_PATH)) {
    Write-Warning "Enemy model not found: $($env:FPS_VISION_ENEMY_MODEL_PATH). Jobs will run with empty detections. Re-run setup-vision.ps1."
}

if ($Install) {
    & $python -m pip install -r requirements.txt
}

Write-Host "device=$($env:FPS_VISION_DEVICE) model=$($env:FPS_VISION_ENEMY_MODEL_PATH)"
Write-Host "media_root=$($env:FPS_VISION_MEDIA_ROOT)"
& $python -m uvicorn app.main:app --host 127.0.0.1 --port 8000
