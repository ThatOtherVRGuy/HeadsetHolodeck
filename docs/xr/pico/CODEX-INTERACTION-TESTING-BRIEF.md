# Interaction-testing brief for the Codex session

Handover from the DeepSeek session, 2026-10-31. You are taking **interactive emulator testing**; the
DeepSeek session keeps implementing code. Everything below is verified fact unless marked *unverified*.

---

## 1. What we are building and where it stands

HeadsetHolodeck is a Unity 6 (6000.2.10f1) XR prototype: voice-driven world creation and loading on a
PICO Swan (PICO OS 6), with a Meta build also maintained. Project root: **`D:\Projects\Unity\HeadsetHolodeckDev`**
(moved from `C:\Projects` today; `C:\Projects` still exists but is stale — do not use it).

Speech path: local Sherpa-ONNX ASR → local grammar or OpenAI intent → `VoiceCommandRouter` →
`WorldActionDispatcher`. The UI is a world-space LCARS panel ("HOLODECK OPS").

### Verified on hardware (2026-10-30) and on the emulator (2026-10-31)

| Area | State |
|---|---|
| Controllers are optional | **verified on hardware** — manifest declares `handtracking=1` and no `controller` requirement |
| Eye tracking device | **verified on hardware and emulator** — `CenterEye=tracked`, `Gaze=on` |
| Camera capture | **works on hardware** (WebCamTexture); rotation fix implemented, *unverified* |
| Local-first transcription | implemented, verified by unit-level probe, *not on hardware* |
| **Pointing / UI clicking** | **this is what you are testing** |

### The bug that makes your task important

Interaction never worked, for controllers *or* hands, and nothing logged a warning. Cause found today:

```
XRUIToolkitManager sets XRUIToolkitHandler.uiToolkitSupportEnabled = true (OnEnable)
Every XRI interactor: enableUIInteraction => m_EnableUIInteraction && uiToolkitSupportEnabled
```

`XRUIToolkitManager` existed in exactly one file in the whole project — the XRI sample scene. It was
**not** in `Holodeck.unity` or the rig prefab, so the flag stayed `false`, every interactor's UI
interaction was silently disabled, and rays could track and render while nothing was ever hoverable.

Fixed by `Assets/App/Shared/Command/SpeechIntent/Runtime/XRUIToolkitSupport.cs`, which installs the
component at runtime. **Confirmed working on the emulator**: the diagnostic now reports
`pointer=uiToolkit=true`.

---

## 2. What we need from you

Prove, in the emulator, that a controller ray can **hover and select UI**, and — if the emulator
supports it — that gaze hover also works.

The emulator is supposed to emulate controller pointing from the host mouse, with a keyboard shortcut as
the trigger/select button. Confirm the actual mapping and record it (it is undocumented here).

### Pass criteria

| # | Check | Evidence |
|---|---|---|
| 1 | A visible ray comes from the controller | your observation / screenshot |
| 2 | Moving the mouse over a panel button highlights it | your observation |
| 3 | Pressing the trigger activates the highlighted button | the panel changes, e.g. **MY WORLDS** opens the saved-worlds list |
| 4 | The beacon reports the pointer live | `pointer=…,RightController/Near-Far=on,…` and `uiToolkit=true` |
| 5 | *(if supported)* Gaze hover | `CameraOffset/Gaze=on` plus a visible highlight that follows your eyes/mouse |
| 6 | *(if supported)* Pinch/grip via emulated hands | `hands=L=tracked` / `R=tracked` |

If check 3 fails while 1–2 pass, that is still a major result: it separates "ray works" from "select
works", which are different subsystems.

---

## 3. Environment: exact state

| Thing | Value |
|---|---|
| Project | `D:\Projects\Unity\HeadsetHolodeckDev` |
| Scene | `Assets/Scenes/Holodeck.unity` |
| APK (latest, includes the pointer fix) | `Builds/Pico-Swan-parity.apk` — 310 MB, built 2026-10-31 21:56 |
| Package id | `com.QLC.HeadsetHolodeckPico` |
| Emulator | `emulator-5554`, PICO OS 6, PICO-Swan emulator image |
| adb | `C:\Users\daren\AppData\Local\Android\Sdk\platform-tools\adb.exe` |
| PICO CLI | `pico-cli` 0.6.1 on PATH; `pico-cli emulator` manages the emulator lifecycle |
| PICO plugin | configured `local` scope, `unity` platform, `us` region, hosts `claude-code` + `codex` |
| Unity CLI | `C:\Users\daren\AppData\Local\Unity\bin\unity.exe` (`unity status`, `unity command …`) |
| PICO MCP | `pico-dev-knowledge` and `unity-mcp` are registered in `.mcp.json` |

### Install and launch

```powershell
adb -s emulator-5554 install -r Builds/Pico-Swan-parity.apk
adb -s emulator-5554 shell am force-stop com.QLC.HeadsetHolodeckPico
adb -s emulator-5554 logcat -c
adb -s emulator-5554 shell monkey -p com.QLC.HeadsetHolodeckPico -c android.intent.category.LAUNCHER 1
```

### Watch the evidence

```powershell
adb -s emulator-5554 logcat -v time -s Unity
```

Every 5 seconds the app logs one self-contained line:

```
[Diag] fps=27 | state=Idle | view=splat=no,pano=no | world='Holodeck 2026-10-31 22:44',type=static,objects=0 |
hands=L=lost,R=lost,aimL=n/a,aimR=n/a |
xrnodes=Head=tracked,LeftHand=untracked,RightHand=tracked,CenterEye=tracked |
controllers=2[PICO4UltraTouchControllerOpenXR:66,…:67] |
pointer=uiToolkit=true,RightController/Near-Far=on,RightController/Poke=on,CameraOffset/Gaze=on,
modality[LCtrl=off,RCtrl=on,LHand=off,RHand=off] | mic=ptt=armed,wake=armed,micDevices=1 | stt=android:no
```

Read it like this:

- `uiToolkit=` must be **true**; if it is false the fix regressed and nothing else matters
- `modality[…]` shows which interactor set XRI enabled; `RCtrl=on` means the controller set is live
- `RightController/Near-Far=on` is the pointer; `…=off` while `xrnodes=RightHand=tracked` would mean a
  modality problem, which is a different bug
- `xrnodes=RightHand=untracked` means the emulator is not reporting a tracked controller — try moving the
  mouse / the emulator's controller settings before concluding anything

---

## 4. Gotchas that will waste your time if you ignore them

1. **Permission dialogs block the app.** On first launch a PICO VR permission dialog takes focus and the
   app stops progressing (no beacon lines). Grant ahead of time:
   ```powershell
   adb -s emulator-5554 shell pm grant com.QLC.HeadsetHolodeckPico android.permission.CAMERA
   adb -s emulator-5554 shell pm grant com.QLC.HeadsetHolodeckPico android.permission.RECORD_AUDIO
   ```
   or tap **Allow** (find it with `uiautomator dump`; the button sits near `(692,474)` on 2160×2158).
2. **`adb shell input tap` does NOT reach XR UI.** Tested today: a tap on the MY WORLDS button produced no
   UI reaction at all, because it injects a touchscreen event and XR UI is driven by an XR ray interactor.
   Do not conclude "clicking is broken" from an adb tap — use the emulator's own mouse/keyboard.
3. **The emulator microphone is silent.** Voice input cannot be tested here; the app logs
   `MicrophoneSource: all native sources silent`. Voice is covered separately by recorded fixtures.
   Do not chase it.
4. **Screenshots**: `adb exec-out screencap -p` works on the emulator (it does not work on the hardware
   headset). Use it as visual evidence.
5. **Do not revert** `Assets/XR/Settings/OpenXR Package Settings.asset` — it holds the enabled
   `EyeGazeInteraction` and `HandTracking` Android features.
6. **Do not print `.env` values.** Key names may appear in logs; values never. `ai-game-developer` in
   `.mcp.json` carries a token — do not copy it into any document.
7. **One agent per editor queue.** The DeepSeek session builds the APK through the Unity menu (~15–25 min
   per build). If a command hangs with a 504, a build is in flight; wait rather than restarting the editor.

---

## 5. What to report back

Write results to `docs/agent-worklog.md` (append a section; do not rewrite other agents' text) **and**
add a short `docs/xr/pico/INTERACTION-TEST-RESULTS.md` containing:

- Emulator and image version, plus the keyboard/mouse mapping you discovered
- Each pass criterion with **pass / fail / not supported** and the evidence (log line or screenshot)
- The beacon line captured at the moment of a successful hover and of a successful select
- Anything surprising, especially: ray visible but no hover; hover but no select; hover only at certain
  distances; gaze working or not

That last group is the valuable part — it tells us which subsystem to fix next.

---

## 6. Boundaries

- Do not rebuild the APK unless you change code; if you do, say so in the ledger, because builds block
  the other session's editor commands for ~20 minutes.
- Do not change gameplay, provider, or platform configuration for this test.
- Nothing here requires a code change. If you believe one is needed, record the evidence and the
  proposed change rather than applying it unilaterally.
