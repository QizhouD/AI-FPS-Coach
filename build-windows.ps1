$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$unityProject = Join-Path $projectRoot "UnityClient"
$hubEditors = "C:\Program Files\Unity\Hub\Editor"
$projectVersionFile = Join-Path $unityProject "ProjectSettings\ProjectVersion.txt"
$versionLine = Get-Content -LiteralPath $projectVersionFile |
    Where-Object { $_ -like "m_EditorVersion:*" } |
    Select-Object -First 1
$requiredVersion = ($versionLine -split ":", 2)[1].Trim()

$unityCandidates = @(
    (Join-Path $hubEditors "$requiredVersion\Editor\Unity.exe"),
    "D:\unity eiditor\Editor\Unity.exe"
)
$unityCandidates += Get-Process -Name Unity -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Path -Unique
$unity = $unityCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if (-not $unity) {
    throw "Unity $requiredVersion was not found. Install it or update the editor path in build-windows.ps1."
}

& $unity `
    -batchmode `
    -nographics `
    -quit `
    -projectPath $unityProject `
    -executeMethod FpsAiCoach.Editor.BuildWindows.Perform `
    -logFile -

if ($LASTEXITCODE -ne 0) {
    throw "Unity Windows build failed with exit code $LASTEXITCODE."
}

Write-Host "Build complete: $projectRoot\Builds\Windows\FPS-AI-Coach-Live.exe"
