# Installs the Pico build on a device or emulator and captures launch evidence.
#
# Written for the Friday hardware session, but it works against the Swan emulator too. It touches
# nothing in the Unity project: install, optional credential push, launch, log capture, screenshot.
#
#   pwsh tools/Verify-PicoDevice.ps1 -Serial emulator-5554
#   pwsh tools/Verify-PicoDevice.ps1 -Serial <headset-serial> -PushEnv
#
# Evidence lands in Logs/pico-verify-<timestamp>/.

[CmdletBinding()]
param(
    [string]$ApkPath = "Builds/Pico-Swan-parity.apk",
    [string]$PackageName = "com.QLC.HeadsetHolodeckPico",
    [string]$Activity = "com.unity3d.player.UnityPlayerActivity",
    [string]$Serial,
    [string]$EnvPath = ".env",
    [switch]$PushEnv,
    [int]$SettleSeconds = 40
)

# Native tools (adb) write progress to stderr; Windows PowerShell 5.1 turns that into a terminating
# error under 'Stop', so keep it non-terminating and rely on explicit exit-code checks.
$ErrorActionPreference = 'Continue'

function Resolve-Adb {
    $onPath = Get-Command adb -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $candidates = @(
        "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
        "C:\Program Files\Android\Android Studio\platform-tools\adb.exe"
    )
    foreach ($candidate in $candidates) { if (Test-Path $candidate) { return $candidate } }
    throw "adb not found. Pass it on PATH or install the Android platform-tools."
}

$adb = Resolve-Adb
$devices = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\sdevice$' }
if (-not $devices) { throw "No adb device is attached. Start the emulator or connect the headset." }

if (-not $Serial) {
    $Serial = ($devices[0] -split '\s+')[0]
    Write-Host "No -Serial given; using $Serial"
}

if (-not (Test-Path $ApkPath)) { throw "APK not found: $ApkPath" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = "Logs/pico-verify-$stamp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "[1/6] Installing $ApkPath on $Serial"
& $adb -s $Serial install -r $ApkPath | Select-Object -Last 1

if ($PushEnv) {
    if (-not (Test-Path $EnvPath)) { throw "Env file not found: $EnvPath" }
    $target = "/sdcard/Android/data/$PackageName/files"
    Write-Host "[2/6] Pushing $EnvPath to $target/.env"
    & $adb -s $Serial shell mkdir -p $target | Out-Null
    & $adb -s $Serial push $EnvPath "$target/.env" | Select-Object -Last 1
} else {
    Write-Host "[2/6] Skipping credential push (use -PushEnv to copy .env)"
}

Write-Host "[3/6] Clearing logcat and restarting the app"
& $adb -s $Serial logcat -c
& $adb -s $Serial shell am force-stop $PackageName
& $adb -s $Serial shell am start -n "$PackageName/$Activity" | Select-Object -First 2

Write-Host "[4/6] Settling for $SettleSeconds s"
Start-Sleep -Seconds $SettleSeconds

$pidText = (& $adb -s $Serial shell pidof $PackageName) -join ''
if ([string]::IsNullOrWhiteSpace($pidText)) {
    Write-Warning "The app is not running after launch. Check $outDir for evidence."
} else {
    Write-Host "      app pid: $pidText"
}

Write-Host "[5/6] Capturing logcat"
& $adb -s $Serial logcat -d -v time > (Join-Path $outDir 'logcat.txt')

Write-Host "[6/6] Capturing screenshot"
$png = Join-Path (Resolve-Path $outDir).Path 'screen.png'
# cmd's redirection is binary-safe; PowerShell's '>' would mangle the PNG.
cmd /c "`"$adb`" -s $Serial exec-out screencap -p > `"$png`""

$log = Get-Content (Join-Path $outDir 'logcat.txt')
$checks = [ordered]@{
    'OpenXR session began'   = ($log | Select-String -Pattern 'OnSessionBegin|XR_SESSION_STATE_READY' -Quiet)
    'Scene loaded'           = ($log | Select-String -Pattern 'AfterSceneLoad scene=' -Quiet)
    'Device .env loaded'     = ($log | Select-String -Pattern 'RuntimeDotEnv\] Loaded|EnvLoader\] Loaded' -Quiet)
    'Missing API key'        = -not ($log | Select-String -Pattern 'No API key found' -Quiet)
    'No Unity exceptions'    = -not ($log | Select-String -Pattern 'E/Unity.*Exception' -Quiet)
    'App still running'      = -not [string]::IsNullOrWhiteSpace($pidText)
}

Write-Host ""
Write-Host "Result" -ForegroundColor Cyan
$checks.GetEnumerator() | ForEach-Object {
    $label = if ($_.Value) { 'PASS' } else { 'FAIL' }
    $colour = if ($_.Value) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}" -f $label, $_.Key) -ForegroundColor $colour
}

Write-Host ""
Write-Host "Evidence: $outDir"
Write-Host "Record each case as pass/fail/unsupported in docs/xr/pico/PARITY-CHECKLIST.md."
if ($checks.Values -contains $false) { exit 1 }
