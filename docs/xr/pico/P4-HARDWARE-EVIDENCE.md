# P4 — hardware evidence (2026-10-30)

First session on physical hardware. Emulator evidence is in [P3-EMULATOR-EVIDENCE.md](P3-EMULATOR-EVIDENCE.md);
this file records only what the headset showed.

## Devices

| | |
|---|---|
| Unit 1 | serial `PB331XPGL6150015G`, model **B3110**, PICO OS **6.2.0**, SDK 36, arm64-v8a, 952 GB free |
| Unit 1 clock | read **Sun Oct 18 2026** while the date was 2026-10-30 — a ~12 day skew. Saved worlds are timestamp-named, so their names carry the wrong date, and log timestamps cannot be correlated with wall-clock notes until it is fixed |
| Unit 2 | a replacement, swapped in during the session when unit 1 ran low on battery; serial and clock may differ |

`adb install -r` of the 309 MB APK succeeded on unit 1 over USB after authorising the debugging prompt.

## Finding H1 — the app declared "controllers required", and the OS obeyed

Launching on hardware failed with no process and this in the system log:

```
W ActivityTaskManager: ... Activity not started because the current activity is being kept for the user.
```

The cause was in our own manifest, not the headset. Dumping the built APK:

```
E: meta-data
  android:name="controller"
  android:value=1              <-- "this app requires a controller"
```

and no `handtracking` entry at all. With no controller paired, the OS treated the launch as
unsatisfiable and refused it. **The headset was right.**

## Fix — declare hand tracking, make controllers optional

`PXR_BuildProcessor` writes the `controller` meta-data from three settings:

| Settings | Manifest result |
|---|---|
| HandTracking feature off, or `isHandTracking` off | remove `handtracking`, **`controller=1`** (our state) |
| on, `handTrackingSupportType` = ControllersAndHands | `handtracking=1` **and** `controller=1` |
| on, `handTrackingSupportType` = HandsOnly | `handtracking=1`, **`controller` removed** |

Changed in the project:

- `Assets/Resources/PICOProjectSetting.asset`: `isHandTracking: 0 → 1`, `handTrackingSupportType: 0 → 1`
- `Assets/Resources/PXR_ProjectSetting.asset`: `handTracking: 0 → 1`

Verified in the rebuilt APK (`Logs/apk-manifest-new.txt`):

```
E: meta-data android:name="handtracking" android:value=1
(no "controller" meta-data)
```

Committed as `c942cd8`; artifact `Builds/HeadsetHolodeckPico-hands-2026-10-30-c942cd8.apk`
(SHA-256 `66180AB8796E40D7E282B77B02237130D703138E6B453E2F91635E3C45166716`).

**Not yet verified on hardware** — unit 1 was swapped out before this build was installed, so the next
step is to install it and confirm (a) it launches with no controller requirement, (b) hands are tracked,
(c) controllers still work if present. If controllers turn out to be *unusable* rather than merely
optional under `HandsOnly`, the fallback is a post-build manifest patch setting `controller=0` while
keeping `handtracking=1`.

## Also worth knowing

- The first unit showed the controller dialog before this fix; that dialog is the correct behaviour for a
  manifest that declares `controller=1`, which is worth remembering before blaming the device.
- The debug command channel is still enabled in this artifact (`files/debug_command.txt`), which is how
  commands are driven while the microphone is unverified on hardware.

## Next on hardware

1. Install `c942cd8` and confirm launch with **no controllers and no hands** raised (the requirement is gone).
2. Hands: `[Diag] hands=`, `xrnodes=`, and whether pointing reports `aim=native` or `aim=derived`.
3. Microphone: push-to-talk, transcript text, wake word.
4. First load of a world that has never been loaded, on real hardware.
5. Save → force-stop → restore on the headset.
