# Pico CLI 0.6.1 / Swan 6.1.0 Windows launch workaround.
# Keeps SDK and AVD state on C: and avoids saving large RAM snapshots.
[CmdletBinding()]
param([string]$Avd = 'Holodeck_Pico_Swan', [int]$Port = 5590)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:USERPROFILE 'AppData\Local\PICO\sdk\6.1\emulator'
$adb = Join-Path $base 'system-images\platform-tools\adb.exe'
$env:ANDROID_AVD_HOME = Join-Path $env:USERPROFILE '.pico\avd'
$env:ANDROID_SDK_ROOT = Join-Path $base 'system-images'
$env:ANDROID_HOME = $env:ANDROID_SDK_ROOT
$env:PATH = "$base;$base\lib64;$base\lib64\qt\lib;$env:PATH"
if (-not (Test-Path -LiteralPath (Join-Path $env:ANDROID_AVD_HOME "$Avd.avd"))) { throw "AVD missing: $Avd" }
$serial = "emulator-$Port"
$devices = & $adb devices
if ($devices -match "(?m)^$serial\s") { throw "Port $Port already has an emulator. Check adb -s $serial emu avd name before reusing it." }
$logRoot = Join-Path $env:USERPROFILE '.pico\cli\logs'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$log = Join-Path $logRoot ("holodeck-boot-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
# Call QEMU directly: the CLI's launcher selects an incorrect nested SDK root.
$p = Start-Process -FilePath (Join-Path $base 'qemu\windows-x86_64\qemu-system-x86_64.exe') -WorkingDirectory $base -ArgumentList @('-avd', $Avd, '-sysdir', (Join-Path $base 'system-images\system-images'), '-no-window', '-no-snapshot-save', '-gpu', 'angle_indirect', '-port', "$Port") -WindowStyle Hidden -RedirectStandardOutput "$log.stdout.log" -RedirectStandardError "$log.stderr.log" -PassThru
Write-Output "Started $Avd, PID $($p.Id), serial $serial. Logs: $log.*.log"
