# Pico parity checklist — hardware session

Purpose: the Friday hardware session should be execution, not discovery. Fill one row per case with
**pass**, **fail**, **unsupported**, or **not tested**, plus the evidence path. Do not mark a case pass
from emulator behaviour; the emulator cannot validate microphone input, tracking quality, passthrough,
room alignment or eye calibration.

## Before you start

1. Build the artifact: `Headset Holodeck > Build > Build Pico APK (device)`.
   It writes `Builds/Pico-Swan-parity.apk`, applies the Pico XR configuration, swaps in the Pico
   manifest, and prepares the SDK loader. An undeclared credential mode stops the build by design.
2. Connect the headset, `adb devices`, then:

   ```powershell
   pwsh tools/Verify-PicoDevice.ps1 -Serial <serial> -PushEnv
   ```

   That installs, pushes `.env` to the app's own data directory, launches, and records
   `Logs/pico-verify-<timestamp>/` with `logcat.txt` and `screen.png`.
3. Read logs with `adb -s <serial> logcat -v time -s Unity`. Save/restore decisions appear under
   `[WorldConfigStore]`, `[WorldConfigRestorer]`, `[WorldConfigAutoSave]`; key resolution under
   `[RuntimeDotEnv]` and `[EnvLoader]`.

## Emulator observations (2026-10-27 → 29) — starting point, not acceptance

Recorded in [P3-EMULATOR-EVIDENCE.md](P3-EMULATOR-EVIDENCE.md). Confirmed on the emulator: boot, XR
session, LCARS rendering, device `.env` loading, authenticated `worlds:list`, a voice-driven splat load,
a generated world loading from cache in ~2.3 s, journaled save with corruption recovery, saving the
built-in room as a `static` world, object generation including cache reuse and placement, and 3D/panorama
switching in both directions.

**Still emulator-only assumptions that hardware must settle:** microphone input, tracking quality,
passthrough, room alignment, eye calibration, frame timing, and whether a first load of a new world stays
responsive (one emulator attempt was killed by the OS watchdog, two later ones succeeded, so it remains
unexplained rather than fixed).

Use that document only to know what is already proven; fill the table below on hardware.

## Cases

| # | Case | Steps | Expected | Result | Evidence |
|---|---|---|---|---|---|
| 1 | Cold launch | Launch from the app list | LCARS panel appears, no controller dialog, no crash | | |
| 2 | Stereo + tracking | Wear, look around | Stable stereo, head tracking, no drift | | |
| 3 | Floor / spawn | Look down, walk | Floor under feet, spawn sane | | |
| 4 | Controllers | Pair, press buttons | Pointer ray, buttons, select | | |
| 5 | Hands | Put controllers down | Pinch/grip recognised | | |
| 6 | Voice wake | Say "computer, …" | Wake detected, listening state | | |
| 7 | Push-to-talk | Press the bound button | Records, then processes | | |
| 8 | Local wake/VAD/ASR | Speak a reserved command | Local parser handles it | | |
| 9 | TTS | Trigger a spoken response | Audio through the headset | | |
| 10 | Cached world load | "load `<saved world>`" | Cached splat renders, floor/collision apply | | |
| 11 | Panorama load + switching | Load a cached world, say "show panorama", then "show 3d" | Panorama sphere appears, then the splat returns | | |
| 12 | World generation | "create a …" | Generation runs, world appears | | |
| 13 | Object generation | "add a …" | Model generated, cached, placed, grabbable | | |
| 14 | Manipulation | "grab world", two-hand scale | World moves/rotates/scales | | |
| 15 | Audio placement | Add a sound, save | Plays in place, persists after restart | | |
| 16 | Save | "save this world" | Journaled write; `world.json` + `.bak` + `.lock` | | |
| 17 | Restart/restore | Force-stop, relaunch | World, spawn, objects, audio return | | |
| 18 | Backup recovery | Corrupt `world.json`, relaunch | Recovers from `.bak`, logs the recovery | | |
| 19 | Repeated cycles | 3× load/unload | No leak, no crash | | |
| 20 | Passthrough / camera | Open camera, capture | Permission prompt, image captured | | |
| 21 | Performance | Note frame feel + `adb shell dumpsys gfxinfo` | Stable, no thermal collapse | | |
| 22 | Cached object reuse | Say the same object prompt twice, answer "use saved" | Re-imports from cache, **no credit preflight**, no exception | | |
| 23 | Object placement | Ask for an object with no location | Appears ~1 m in front of the gaze, facing the viewer, resting on the surface (no fall) | | |
| 24 | Metric scale + grounding | Load a freshly generated world | `Applied splat semantics … metric_scale_factor=…`; world is life-sized and sits on the floor | | |
| 25 | First load of a new world | Load a world never loaded before | Completes without a watchdog kill or long freeze; note the seconds | | |
| 26 | Panorama on a fresh world | After case 25, "show panorama" | Panorama available without a separate download | | |
| 27 | Save the empty room | With no world loaded, add a sound, "save this world" | Saved under a generated name; `world_source.type = "static"`, `objects` contains the sound | | |
| 28 | Static world restore | Force-stop, relaunch, load that world | Room returns with the sound playing | | |

## Known emulator gaps to re-check on hardware

- **Controllers Required** dialog appears when no controllers are paired. Confirm behaviour with a
  real controller and with hands-only.
- Voice pipeline: the emulator granted `MICROPHONE`, but mic input was never validated.
- The app is ARM64-only; hardware runs it natively, so any timing measured under translation is not
  representative.

## Recording

Append results to `docs/xr/pico/` with the build's commit, the device OS version, and the evidence
path. Separate emulator evidence from hardware evidence in every record.
