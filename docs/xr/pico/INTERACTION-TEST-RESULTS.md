# PICO Emulator Interaction Test Results

Date: 2026-10-31  
Tester: Codex interactive session

## Environment

| Item | Value |
|---|---|
| AVD | `Holodeck_Pico_Swan` |
| Emulator | PICO Emulator `6.1.0` |
| Device | `emulator-5554` |
| Product | `swan_oversea` / `swan_oversea_x86_64` |
| Android | 16 |
| Installed package | `com.QLC.HeadsetHolodeckPico` |
| APK under test | `Builds/Pico-Swan-parity.apk` |

The app was already installed and running when testing began. The emulator process had exited after
the handover; `pico-cli emulator start --avd Holodeck_Pico_Swan` restored it without rebuilding or
reinstalling the APK.

## Input mapping

The bottom interaction-mode menu contains `Left Controller Mode`, `Right Controller Mode`, and
`Eye Gesture Mode`.

- In `Right Controller Mode`, moving the host mouse aims the controller ray and left-click simulates
  the controller confirm button.
- The left-side gamepad button is a separate `Controller Button Mode` toggle. PICO's reference guide
  states that this toggle controls keyboard-to-controller-button mapping and does not itself select
  the controller interaction mode.
- In `Eye Gesture Mode`, PICO documents mouse movement as gaze aiming and left-click as pinch.
- Enabling the left-side gamepad button opens a separate `PicoExtendedWindow` named `Setting`, with
  the selected `Controller Setting` page. The displayed mapping was:

  | Controller input | Keyboard key |
  |---|---|
  | Left grip | `E` |
  | Left trigger | `G` |
  | Right grip | `C` |
  | Right trigger | `U` |

  The key mapping becomes active only when the bottom mode is also set to `Left Controller Mode` or
  `Right Controller Mode`; it is not active in `Eye Gesture Mode`.

## Results

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Visible controller ray | **PASS** | The ray appeared in the emulator window in `Right Controller Mode`. The ray is a host-side emulator overlay and is not present in `pico-cli capture screenshot` output. |
| 2 | Mouse movement produces UI hover | **PASS** | Moving the ray over `OBJECTS` changed that label's rendered state while the rest of the menu remained unchanged. See `interaction-controller-hover-objects.png`. |
| 3 | Left click activates the highlighted button | **PASS** | Left-clicking `OBJECTS` changed the content from the saved-world list to `OBJECT CATALOG`. Returning to the world list and left-clicking `MY WORLDS` opened the saved-worlds list again. See `interaction-select-objects.png` and `interaction-controller-select-my-worlds.png`. |
| 4 | Beacon reports the live pointer | **PASS** | The hover and selection beacon reported `pointer=uiToolkit=true`, `LeftController/Near-Far=on`, `RightController/Near-Far=on`, `RightController/Poke=on`, and `modality[LCtrl=on,RCtrl=on,...]`. |
| 5 | Gaze hover and pinch | **FAIL** | `Eye Gesture Mode` was active and the beacon reported `CameraOffset/Gaze=on`, but the emulator showed neither a ray nor a clear hover highlight. Left-click on the aimed `OBJECTS` tab did not change the panel. See `interaction-gaze-hover-objects.png` and `interaction-gaze-select-objects.png`. |
| 6 | Emulated hands tracked | **NOT SUPPORTED IN THIS RUN** | `Eye Gesture Mode` still reported `hands=L=lost,R=lost` and `xrnodes=...LeftHand=untracked,RightHand=untracked...`. No tracked XR hand pose was available to exercise pinch or grip. |

## Beacon at controller hover

```text
10-31 23:15:40.591 I/Unity   (20023): [Diag] fps=23 | state=Idle | view=splat=no,pano=no | world='Holodeck 2026-10-31 22:44',type=static,objects=0 | hands=L=lost,R=lost,aimL=n/a,aimR=n/a | xrnodes=Head=tracked,LeftHand=tracked,RightHand=tracked,CenterEye=tracked | controllers=2[PICO4UltraTouchControllerOpenXR:66,PICO4UltraTouchControllerOpenXR1:67] | pointer=uiToolkit=true,LeftController/Near-Far=on,RightHand/Near-Far=off,RightController/Near-Far=on,RightHandTeleport/HandTeleport=off,RightHand/Poke=off,LeftHand/Poke=off,RightController/Teleport=off,LeftHandTeleport/HandTeleport=off,CameraOffset/Gaze=on,LeftController/Teleport=off,modality[LCtrl=on,RCtrl=on,LHand=off,RHand=off],LeftController/Poke=on,RightController/Poke=on,LeftHand/Near-Far=off | mic=ptt=armed,wake=armed,micDevices=1 | stt=android:no | camera=cam=(0.9,1.5,2.7)
```

## Beacon at successful controller selection

```text
10-31 23:18:36.954 I/Unity   (20023): [Diag] fps=15 | state=Idle | view=splat=no,pano=no | world='Holodeck 2026-10-31 22:44',type=static,objects=0 | hands=L=lost,R=lost,aimL=n/a,aimR=n/a | xrnodes=Head=tracked,LeftHand=tracked,RightHand=tracked,CenterEye=tracked | controllers=2[PICO4UltraTouchControllerOpenXR:66,PICO4UltraTouchControllerOpenXR1:67] | pointer=uiToolkit=true,LeftController/Near-Far=on,RightHand/Near-Far=off,RightController/Near-Far=on,RightHandTeleport/HandTeleport=off,RightHand/Poke=off,LeftHand/Poke=off,RightController/Teleport=off,LeftHandTeleport/HandTeleport=off,CameraOffset/Gaze=on,LeftController/Teleport=off,modality[LCtrl=on,RCtrl=on,LHand=off,RHand=off],LeftController/Poke=on,RightController/Poke=on,LeftHand/Near-Far=off | mic=ptt=armed,wake=armed,micDevices=1 | stt=android:no | camera=cam=(0.9,1.5,2.7)
```

## Findings

1. **Controller hover and select now work.** This clears the original silent UI-interaction failure:
   the XR UI toolkit is enabled and the controller interactor is live.
2. **The saved-world list was initially open.** Clicking `MY WORLDS` while it was already active could
   not prove a transition, so `OBJECTS -> OBJECT CATALOG -> MY WORLDS` was used as the unambiguous
   sequence.
3. **`pico-cli capture screenshot` omits the host-side controller-ray overlay.** The ray was visible
   in the emulator window, but the device framebuffer capture contains only the XR output. The saved
   screenshots show the hovered tab and confirmed panel transitions.
4. **Gaze reaches the XR input layer but not the Holodeck panel.** The beacon reports
   `CameraOffset/Gaze=on`, yet gaze produced no visible hover and pinch did not activate the aimed
   button. This is a separate app-side gaze/UI integration gap, not evidence that controller XRI
   interaction is broken.
5. **XR hand poses are unavailable in this emulator run.** The Eye Gesture Mode beacon reported both
   hands lost/untracked, so hand pinch and grip remain unverified.

## Keyboard trigger/grip injection check

With `Right Controller Mode` and `Controller Button Mode` both active, the controller ray was aimed
at `OBJECTS`. Injecting the displayed `U`, `G`, and `C` bindings did not activate the tab, including a
repeated `U` attempt. Clicking the mouse at the same aimed position still did activate controller UI.

This does not invalidate the shortcut mechanism: PICO exposes and saves the trigger/grip bindings,
and the initial automation press is a very short key tap. XRI reads trigger state per frame, so a
proper next check is a deliberate key-down, short hold, then key-up rather than a synthetic tap. The
current Computer Use API does not expose separate key-down/key-up calls, and neither the PICO
knowledge MCP nor the Unity MCP exposes emulator input injection.

For emulated hand pinch, use `Eye Gesture Mode` and a left mouse click, as documented by PICO. That is
the direct pinch substitute; controller trigger keys apply only in the two controller modes.

## Potential follow-up

1. Use PICO's Controller Settings GUI, or a helper that can hold a key across frames, to verify the
   mapped trigger and grip bindings independently of a synthetic key tap.
2. Inspect how the LCARS buttons advertise pointer/drag handlers to XRI and add or verify a gaze
   interactor path. The rig contains a `Gaze Interactor`, but the test build did not visibly hover or
   activate the panel in Eye Gesture Mode. Keep this separate from controller-ray routing because
   controller hover and select are confirmed working.

## Round 2 — eye-device driver and master volume

Date: 2026-11-01  
Tester: Codex interactive session

### Artifact

| Item | Value |
|---|---|
| APK | `Builds/Pico-Swan-parity.apk` |
| SHA-256 | `8013DFAB13EAAA1B43F510252A1D6A55C8DDF2DA641E29046C50905BA0155A46` |
| Size | 324,837,000 bytes |
| Package | `com.QLC.HeadsetHolodeckPico` |
| Package version | `9.2.1` |
| Emulator | `emulator-5554`, Swan 6.1.0, Android 16, `swan_oversea_x86_64` |

The APK was reinstalled with `adb install -r`, and the camera and microphone permissions were granted
before launch. No rebuild or source change was made.

### Results

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Eye-device pose reaches the gaze interactor | **PASS** | Startup logged `[EyeGaze] using eye pose (0.00,1.65,0.00).` The driver installed, detached `Gaze Stabilized`, and did not use the head-gaze fallback. |
| 2 | Gaze hover | **FAIL** | In Eye Gesture Mode, aiming at `OBJECTS` and then `MY WORLDS` produced no panel highlight. The beacon reported `CameraOffset/Gaze=on`, `CenterEye=tracked`, and `RightHand=untracked`. |
| 3 | Gaze select | **FAIL** | Left-clicking while aimed at `MY WORLDS` left the panel on `OBJECT CATALOG`. See `interaction-round2-gaze-no-select-my-worlds.png`. |
| 4 | Right Controller Mode regression | **PASS** | Switching back to Right Controller Mode restored `RightHand=tracked`; aiming and confirming `OBJECTS` changed the panel to `OBJECT CATALOG`. See `interaction-round2-controller-select-objects.png`. |
| 5 | Master volume command channel | **PASS** | `mute` logged `1.00 -> 0.00`; `unmute` logged `0.00 -> 1.00`; `softer` logged `1.00 -> 0.90`; the restoring `louder` logged `0.90 -> 1.00`. |
| 6 | Held `U`/`G`/`C` trigger test | **NOT TESTED** | The available Computer Use API exposes a key chord but no separate key-down/key-up, so a hold across XR frames was still not possible. |

The two Round 2 screenshots are byte-identical
(`SHA-256 9D778871869BC0D0A2E4563F89B27665AFFF2A7A13A4170EBB43CD27E25DF5CA`, 428,389 bytes each):
the panel remained on `OBJECT CATALOG` after the eye-gaze attempt, while the controller regression
had already selected that same panel state.

### Round 2 eye mode beacon

```text
[Diag] fps=60 | state=Idle | view=splat=no,pano=no |
world='Holodeck 2026-11-01 12:43',type=static,objects=0 |
hands=L=lost,R=lost,aimL=n/a,aimR=n/a |
xrnodes=Head=tracked,LeftHand=untracked,RightHand=untracked,CenterEye=tracked |
controllers=2[PICO4UltraTouchControllerOpenXR:66,PICO4UltraTouchControllerOpenXR1:67] |
pointer=uiToolkit=true,RightController/Near-Far=on,RightController/Poke=on,CameraOffset/Gaze=on,
modality[LCtrl=off,RCtrl=on,LHand=off,RHand=off] |
mic=ptt=armed,wake=armed,micDevices=1 | stt=android:no | camera=cam=(0.9,1.5,2.7)
```

### Round 2 controller mode beacon

```text
[Diag] fps=58 | state=Idle | view=splat=no,pano=no |
world='Holodeck 2026-11-01 12:43',type=static,objects=0 |
hands=L=lost,R=lost,aimL=n/a,aimR=n/a |
xrnodes=Head=tracked,LeftHand=untracked,RightHand=tracked,CenterEye=tracked |
controllers=2[PICO4UltraTouchControllerOpenXR:66,PICO4UltraTouchControllerOpenXR1:67] |
pointer=uiToolkit=true,RightController/Near-Far=on,RightController/Poke=on,CameraOffset/Gaze=on,
modality[LCtrl=off,RCtrl=on,LHand=off,RHand=off] |
mic=ptt=armed,wake=armed,micDevices=1 | stt=android:no | camera=cam=(0.9,1.5,2.7)
```

### Exact volume log

```text
[DebugCommandIntentBridge] Received command: 'mute'
[Audio] master volume 1.00 -> 0.00 (action='mute')
[DebugCommandIntentBridge] Received command: 'unmute'
[Audio] master volume 0.00 -> 1.00 (action='unmute')
[DebugCommandIntentBridge] Received command: 'louder'
[Audio] master volume 1.00 -> 1.00 (action='louder')
[DebugCommandIntentBridge] Received command: 'softer'
[Audio] master volume 1.00 -> 0.90 (action='softer')
[DebugCommandIntentBridge] Received command: 'louder'
[Audio] master volume 0.90 -> 1.00 (action='louder')
```

### Round 2 finding

The new driver fixes the source of the gaze transform: the emulator supplies a tracked `CenterEye`
pose and the code reads it rather than falling back to head gaze. It does not yet make the Holodeck
panel hoverable in Eye Gesture Mode. Controller selection remains functional, so the remaining failure
is below or outside the gaze pose acquisition itself. One anomaly worth carrying forward is that the
beacon still reports `modality[RCtrl=on]` in Eye Gesture Mode even though `RightHand=untracked` there.

The Android broadcast path did not register on this API 36 emulator, but the file channel was
unaffected:

```text
[DebugCommandIntentBridge] Registration failed: java.lang.NoSuchMethodError: no non-static method
with name='registerReceiver' signature='(Landroid.content.BroadcastReceiver;Landroid.content.IntentFilter;I)V'
in class Ljava.lang.Object;
```
