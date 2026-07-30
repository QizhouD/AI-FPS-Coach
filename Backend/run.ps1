$ErrorActionPreference = "Stop"
$backendRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $backendRoot

if (-not (Test-Path -LiteralPath ".venv")) {
    python -m venv .venv
}

$python = Join-Path $backendRoot ".venv\Scripts\python.exe"
& $python -m pip install -r requirements.txt
& $python -m uvicorn app.main:app --host 127.0.0.1 --port 8000
