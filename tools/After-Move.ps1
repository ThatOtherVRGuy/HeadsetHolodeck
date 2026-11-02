# Run AFTER moving C:\Projects to the new drive, and after closing Unity.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\After-Move.ps1 `
#       -OldPath 'C:\Projects\Unity\HeadsetHolodeckDev' -NewPath 'D:\Projects\Unity\HeadsetHolodeckDev'
#
#   ... -Apply                                  actually rewrite files
#   ... -Apply -MoveGradle -GradlePath D:\Gradle also move the ~25 GB gradle cache
#
# Without -Apply it only reports, so the first run is always safe.
#
# Note: this was written but could not be executed inside the agent sandbox (ConstrainedLanguage blocks
# .NET calls). Run it with -Apply omitted first and read the report.

param(
    [Parameter(Mandatory = $true)][string]$OldPath,
    [Parameter(Mandatory = $true)][string]$NewPath,
    [switch]$Apply,
    [switch]$MoveGradle,
    [string]$GradlePath = 'D:\Gradle'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

Write-Host "=== repo ===" -ForegroundColor Cyan
Write-Host "  $repo"
if (-not (Test-Path (Join-Path $repo 'Assets'))) {
    Write-Host "  ERROR: no Assets folder next to $PSScriptRoot" -ForegroundColor Red
    exit 1
}
if ($repo -ne $NewPath) {
    Write-Host "  WARNING: running from '$repo' but the expected new location is '$NewPath'" -ForegroundColor Yellow
    Write-Host "           (expected if you are dry-running before the move)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== files containing the old absolute path ===" -ForegroundColor Cyan
$hits = @()
$files = Get-ChildItem $repo -Recurse -File -Include *.json,*.ps1,*.cs,*.md,*.yml -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\Library\\|\\Temp\\|\\obj\\|\\Logs\\|\\.git\\' }

foreach ($file in $files) {
    $match = Select-String -LiteralPath $file.FullName -Pattern $OldPath -SimpleMatch -List
    if (-not $match) { continue }
    $relative = $file.FullName.Substring($repo.Length).TrimStart('\')
    $hits += $file.FullName
    Write-Host "  $relative"

    if ($Apply) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        $updated = $text.Replace($OldPath, $NewPath)
        if ($updated -ne $text) {
            # UTF8 without BOM: Node reads .mcp.json and a BOM would break JSON.parse.
            $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)
            Write-Host "    rewritten" -ForegroundColor Green
        }
    }
}
if ($hits.Count -eq 0) { Write-Host "  none" -ForegroundColor Green }
elseif (-not $Apply) { Write-Host "  dry run - re-run with -Apply to rewrite these" -ForegroundColor Yellow }

Write-Host ""
Write-Host "=== gradle cache (stays on C: unless moved) ===" -ForegroundColor Cyan
$gradleHome = Join-Path $env:USERPROFILE '.gradle'
if (Test-Path $gradleHome) {
    $sum = (Get-ChildItem $gradleHome -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    Write-Host ("  {0} = {1} GB" -f $gradleHome, [int]($sum / 1GB))
    if ($MoveGradle) {
        if (Test-Path $GradlePath) {
            Write-Host "  $GradlePath already exists, not moving" -ForegroundColor Yellow
        }
        else {
            Write-Host "  moving to $GradlePath (this takes a few minutes)..."
            robocopy $gradleHome $GradlePath /E /MOVE /R:1 /W:1 | Out-Null
            cmd /c "setx GRADLE_USER_HOME `"$GradlePath`""
            Write-Host "  moved; GRADLE_USER_HOME set for the user (restart shell + Unity)" -ForegroundColor Green
        }
    }
    else { Write-Host "  pass -MoveGradle to move it and set GRADLE_USER_HOME" -ForegroundColor Yellow }
}
else { Write-Host "  no ~/.gradle found" -ForegroundColor Green }

Write-Host ""
Write-Host "=== still to do by hand ===" -ForegroundColor Cyan
Write-Host "  1. Unity Hub: add $NewPath, remove the old entry"
Write-Host "  2. Let Unity reimport (15-25 min) if Library/ was deleted; keep it if not"
Write-Host "  3. unity status   -> should list this project with a ready port"
Write-Host "  4. Build once, install, confirm the beacon line shows pointer=uiToolkit=true"

Write-Host ""
Write-Host "=== git state ===" -ForegroundColor Cyan
Push-Location $repo
try { git status --short } finally { Pop-Location }
