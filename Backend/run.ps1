# Demo-analysis entrypoint. On the GPU gaming PC, do not use this for a first
# install: run setup-vision.ps1 then run-vision.ps1 so CUDA torch is installed
# before ultralytics pulls a CPU wheel.
$ErrorActionPreference = "Stop"
$backendRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $backendRoot

$nvidia = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if ($nvidia) {
    Write-Host "NVIDIA GPU detected. For CUDA vision use .\setup-vision.ps1 then .\run-vision.ps1. This script can install CPU torch on a fresh venv."
}

if (-not (Test-Path -LiteralPath ".venv")) {
    python -m venv .venv
}

$python = Join-Path $backendRoot ".venv\Scripts\python.exe"
& $python -m pip install -r requirements.txt
& $python -m uvicorn app.main:app --host 127.0.0.1 --port 8000
