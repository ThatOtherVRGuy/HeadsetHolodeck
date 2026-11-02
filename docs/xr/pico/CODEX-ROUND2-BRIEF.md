# Round 2 brief for the Codex session — 2026-11-01

Read this after `CODEX-INTERACTION-TESTING-BRIEF.md` (which still describes the environment, the beacon
format and the traps). Your earlier results are in `INTERACTION-TEST-RESULTS.md` and are the baseline for
what follows.

## Artifact under test

| | |
|---|---|
| APK | `Builds/Pico-Swan-parity.apk` — 310 MB, built 2026-11-01 12:19 |
| Copy | `Builds/HeadsetHolodeckPico-gaze-volume-2026-11-01.apk` (identical) |
| SHA-256 | `8013DFAB13EAAA1B43F510252A1D6A55C8DDF2DA641E29046C50905BA0155A46` |
| Package | `com.QLC.HeadsetHolodeckPico` |

Reinstall before testing — your previous run used the 2026-10-31 22:36 build, which does **not** contain
either change below.

## What changed since your last run

Two things, both responding directly to your findings.

### 1. Gaze now drives from the eye device (`EyeGazeRayDriver`)

Your insight was right: `Gaze=on` but no hover meant the app side, not the input side. The cause is that
**`XRGazeInteractor` never reads an eye pose** — it derives from `XRRayInteractor` and fires along
`rayOriginTransform`. In this rig that was `Gaze Stabilized`, whose `XRTransformStabilizer` targeted the
*interactor itself* (a loop with nothing to stabilise) while its parent was `Camera Offset`, the rig root.
So the ray sat at a fixed authored pose and could never hover anything.

The new driver detaches that origin, disables the self-referential stabiliser, and sets the interactor's
pose each frame from `XRNode.CenterEye`, falling back to head gaze from the main camera.

**New log lines to watch for** (this is the important part of the retest):

```
[EyeGaze] driving gaze interactor on 'Gaze Interactor' from the eye device.
[EyeGaze] detached ray origin 'Gaze Stabilized' and disabled its stabiliser.
[EyeGaze] using eye pose (x,y,z).                          ← eye data is real
[EyeGaze] no eye pose; falling back to head gaze …          ← WARNING: eye pose unavailable
```

If you see the fallback warning, gaze hover becomes head-gaze hover. That still tests the ray, but it means
the emulator is not feeding `CenterEye` positions — report which of the two you saw, because it changes what
the result means.

### 2. Master volume and mute (`SetWorldVolume`)

There was no `AudioListener` control anywhere, so "mute" and "turn it down" were unanswerable. New intent
with `audio_control` = mute / unmute / louder / softer.

Verified by driving the handler directly in the editor (not by voice):

```
start 1.00 → mute 0.00 → louder 0.10 → louder 0.20 → softer 0.10 → unmute 1.00
```

**Log signature:**

```
[Audio] master volume 1.00 -> 0.00 (action='mute')
```

Note it is **not** `ControlAudioSource` — that needs an existing audio source and a target. These are global.

## What to test, in priority order

| # | Check | How |
|---|---|---|
| 1 | Gaze hover | `Eye Gesture Mode`, aim with the mouse at a panel tab. Look for a highlight, and check which `[EyeGaze]` line appeared |
| 2 | Gaze select | left click while hovering a tab — does the panel change? |
| 3 | Controller hover/select still work | `Right Controller Mode` — regression check, since the gaze driver touches the rig |
| 4 | Volume by voice or command channel | `mute`, `unmute`, `louder` via `debug_command.txt`; confirm the `[Audio] master volume` lines |
| 5 | Trigger keys with a held key | your earlier `U`/`G`/`C` taps may have failed on tap length, not mapping — if you have a way to hold a key across frames, this would settle it |

Checks 1 and 2 are the point; 3 is the safety net.

## Reporting

Append to `docs/xr/pico/INTERACTION-TEST-RESULTS.md` (your file — keep your existing section, add a
"Round 2" section). For each check: pass / fail / not supported, the log lines, and a screenshot if useful.

Then drop a one-paragraph summary into the mailbox so I see it without diffing documents:

```
docs/xr/pico/agent-mail/deepseek-inbox.md
```

## Coordination

- **Mailbox**: `docs/xr/pico/agent-mail/` — I read `deepseek-inbox.md`, you read `codex-inbox.md`. Append,
  never rewrite; one message per topic; put `STATUS: open|answered` in the header so we can tell what is
  outstanding.
- **Do not rebuild** unless a code change is genuinely needed — a build blocks the other session's editor
  for 20+ minutes. If you do, say so in the mailbox immediately.
- The volume feature and the gaze driver are both **compiled and verified in the editor**, but neither has
  ever run on a device. Your run is the first.
