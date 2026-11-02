# Pre-test handover for the PICO team

Purpose: answer one question first — **does the build run on the device at all?** Everything below is in
service of that, plus what to send back so the answer is actionable.

## The artifact

| | |
|---|---|
| Package | `com.QLC.HeadsetHolodeckPico` |
| Build | `Builds/Pico-Swan-parity.apk` |
| Target | PICO OS 6 / Swan, ARM64 only (no x86), Vulkan, OpenXR MultiPass |
| min SDK / target SDK | 32 / 36 |
| Credentials | **none embedded** — `device` credential mode |

**No API keys are in the APK.** That is deliberate, and it does not affect "does it run": launch, rendering,
tracking, hands, controllers and local content all work without keys. Only the AI features (voice,
world/object generation, audio search) need a `.env`, which is not part of this handover.

## Install and launch

```powershell
adb install -r Builds/Pico-Swan-parity.apk
adb shell monkey -p com.QLC.HeadsetHolodeckPico -c android.intent.category.LAUNCHER 1
```

Launch from the app list is equivalent. First launch after an install does extra work (local model
extraction), so allow up to a minute before judging it.

---

# First-time setup: the `.env` file

Skip this for a "does it run" check. Do it when you want the AI features (voice, world/object generation,
audio search) to work.

## Where it goes

```
/sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/.env
```

That is the app's own external files directory (`Application.persistentDataPath` in Unity terms). The app
reads it **at launch** — after editing or re-pushing it, force-stop the app and relaunch:

```powershell
adb shell am force-stop com.QLC.HeadsetHolodeckPico
adb shell monkey -p com.QLC.HeadsetHolodeckPico -c android.intent.category.LAUNCHER 1
```

Create the `files` directory first if it does not exist (the app creates it on first launch, so launching
once is the easiest way):

```powershell
adb shell mkdir -p /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files
```

## Format

One `KEY=value` per line. `#` starts a comment. Quoting is optional (`EnvLoader` strips surrounding
quotes; values are trimmed).

```
# Minimum for voice + world generation
OPENAI_API_KEY=sk-...
WORLDLABS_API_KEY=...

# Optional: audio search, object generation
FREESOUND_API_KEY=...
THREEDAISTUDIO_API_KEY=...
```

**Save it without a byte-order mark.** Notepad adds a BOM by default, which turns the *first* key into
`﻿OPENAI_API_KEY` and it is silently ignored. Use VS Code, Notepad++ ("UTF-8" without BOM), or PowerShell 7:

```powershell
Set-Content .env "OPENAI_API_KEY=..." -Encoding utf8   # PowerShell 7 writes UTF-8 without BOM
```

Windows line endings are fine (each line is trimmed).

## Pushing it

From a PC with the headset connected:

```powershell
adb push .env /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/.env
```

Without a file on disk:

```powershell
adb shell "echo 'OPENAI_API_KEY=sk-...' > /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/.env"
adb shell "echo 'WORLDLABS_API_KEY=...' >> /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/.env"
```

Verify it landed, and check the size looks like a text file:

```powershell
adb shell cat /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/.env
```

## Verifying it was read

At launch the app logs how many variables it loaded and where from:

```
[RuntimeDotEnv] Loaded 15 variable(s) from /storage/emulated/0/Android/data/com.QLC.HeadsetHolodeckPico/files/.env
```

`Loaded 0 variable(s)`, or a `Loaded …` line naming a different path, means the file is missing or in the
wrong place. Key **names** appear in logs; values never do.

## If adb is not an option

`Android/data` is normally hidden from MTP/USB file transfer, and on PICO OS you may not be able to browse
it in the headset's own file manager. Pushing over adb is the reliable route. A local `adb shell` on the
device (Terminal app, if present) works too, with the same path.

## Handling secrets

- The `.env` is **not** inside the APK; it lives only on the device.
- Do not commit it to any repository. The repo ships `.env.example` instead.
- Keys are per-account and revocable: if a device is passed on, delete the file
  (`adb shell rm /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/.env`) and rotate the keys.

---

## What "runs" looks like

1. Splash, then the LCARS operations panel in a room.
2. Head tracking stable; room does not drift.
3. A `[Diag]` line roughly every 5 seconds:

```
[Diag] fps=… | state=… | view=splat=…,pano=… | world=… | hands=… | controllers=… | mic=… | camera=…
```

If that line appears, the app is alive and the rest can be read from it.

## What we would like reported

| Question | How it shows up |
|---|---|
| Does it launch and stay up? | app visible; `[Diag]` lines continuing |
| Is head tracking sane? | `camera=cam=(x,y,z)` changing as you move; no drift |
| Are controllers seen? | `controllers=N[…]` non-zero, or `input device added` lines |
| Can you point and select? | panel buttons respond to the ray |
| Are hands tracked? | `hands=L=tracked` / `R=tracked` when raising a hand |
| Does hand pointing work? | `aimL=native` or `aimL=derived` (both mean pointing works); `none` is the failure |

## What to send back

```powershell
adb logcat -c
adb logcat -v time -s Unity > pretest.log      # start before launching
# …run the app for a minute or two…
```

Send `pretest.log` plus the device model and OS build (`adb shell getprop ro.product.model` and
`ro.build.display.id`). If it crashes, also send `adb logcat -d -b crash`.

## Known gaps — not defects for this test

- **Hand grip** may not register. Pinch has two mechanisms, grab has one, so grabbing is the weakest hand
  interaction.
- **First load of a world never loaded before** was killed once by an OS watchdog on an emulator and
  succeeded twice afterwards; if it stalls, note how long it ran before stopping.
- **Audio** may play at an unexpected volume; volume is not tuned for the device yet.
- The APK accepts commands dropped in `files/debug_command.txt` (a test affordance). It is not a release
  build and that channel is removed before shipping.
