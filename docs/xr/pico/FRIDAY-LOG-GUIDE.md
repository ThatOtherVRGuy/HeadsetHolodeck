# Friday hardware session — log guide

Purpose: the agent may not be attached to the headset, so the log has to answer the questions on its own.
This page says how to capture, what the automatic diagnostics emit, and which lines decide each test.

## Capturing

```powershell
adb -s <serial> logcat -c
adb -s <serial> logcat -v time -s Unity > friday.log     # leave running for the session
```

Send `friday.log` as-is; it is filtered to Unity and stays readable. For a specific failure also capture
`adb -s <serial> logcat -d > friday-full.log` and `adb -s <serial> shell dumpsys gfxinfo com.QLC.HeadsetHolodeckPico`.

Start the app once before the session's first test so the beacon and boot lines are in the file:

```
[Diag] beacon online. device='…' os='…' unity='6000.2.10f1' app='…' interval=5s
[BootProbe] …
```

## The beacon

Every 5 s the app logs one self-contained line. It is the fastest way to tell what state a test started
from, and it keeps a timeline when nothing else fires:

```
[Diag] fps=58 | state=Ready | view=splat=ready,pano=ready | world='purple elephant',type=worldlabs,objects=1 |
       hands=L=tracked,R=lost,aimL=derived,aimR=n/a | controllers=2[Pico4UltraControllerLeft:3,…] |
       mic=ptt=armed,wake=armed,micDevices=1 | camera=cam=(0.2,1.6,0.1)
```

Field meanings, and what a surprise in each means:

| Field | Reading it |
|---|---|
| `state` | `Idle` after a failed command; `Ready` when the app considers itself usable |
| `view=splat=…,pano=…` | exactly the guards the view-switch commands check, so `pano=no` explains "show panorama did nothing" |
| `world` | active config; `type=static` means the built-in room, `objects` counts what is saved |
| `hands` | `no-subsystem` = XR Hands never started; `aim=derived` = the fallback is carrying pointing; `aim=none` = the real failure |
| `controllers` | empty list = no controller devices seen at all, so the profile/mapping was never exercised |
| `mic` | `ptt=off`/`wake=off` mean the trigger components are disabled, not that the microphone failed |

## Per-test lines to look for

| Test | Grep | Pass looks like |
|---|---|---|
| Launch | `beacon online\|BootProbe\|OnSessionBegin` | all three, in that order |
| Controllers | `input device\|controllers=\|ControllerWakeTrigger fired` | a device added, then `controllers=2[…]`, then wake fires |
| Hand tracking | `hands=` | `L=tracked`/`R=tracked` change from `lost` when you raise a hand |
| Hand pointing | `XRHandsGestureSource`, `deriving hand aim`, `aimL=derived` | either native aim or the derived fallback, never `aim=none` |
| Pinch / grab | `HandOnlyInputCoordinator`, `grab`, `pressed` | a grab event when you pinch; silence means grip has no value (known gap) |
| Push-to-talk | `PushToTalkTrigger] Press started`, `Press released`, `Transcript:` | one press, one transcript |
| Wake word | `[WakeDiag] heard=… matched=…` | `matched=true` for an accepted alias |
| Microphone | `MicrophoneSource`, `all native sources silent` | no `silent` lines; `started (device=…)` |
| Load saved world | `Loading saved world`, `Cached splat`, `Restore complete` | restore completes with a splat source |
| Fresh world (first load) | `LoadWorldAsync`, `Applied splat semantics`, `Restore complete` | semantics values logged, restore completes, no watchdog kill |
| Metric scale | `Applied splat semantics` / `Recorded loaded world transform` | non-1 `metric_scale_factor` recorded, and `scale=` matches it |
| 3D ↔ panorama | `RequestPanoView`, `Panorama sphere shown`, `Splat3D path` | sphere shown with a texture size; splat path returns |
| Object generation | `Submitting Tripo`, `status=FINISHED`, `Settled`, `Generated object is ready` | all four |
| Cached object reuse | `I found a saved`, `use_saved`, `Loaded saved` | reuse with **no** `balance:` line |
| Sideload by name | `Resolved '…' →`, `Sideloading`, `Loaded castle` | resolved path plus a subfolder/`sideload folder` note |
| Save | `WorldConfigStore`, `Saved`, `objects=` | journaled write, then `objects=` non-zero in the beacon |
| Restart / restore | `Restore complete`, `objects=` | world, objects and audio return |
| Errors | `E/Unity`, `Exception`, `failed` | nothing unexpected; each failure here is a finding |

## Known gaps to expect (not defects)

- **Grip has no fallback** — it needs the runtime's grasp value; pinch works via joint distance, grab may
  not. If `grab` never fires while pinch does, this is why.
- **First load of a never-loaded world** was killed once by the OS watchdog on the emulator and succeeded
  twice afterwards. If it stalls on hardware, note the seconds elapsed; the log will show whether the
  download finished.
- **Emulator-only evidence does not transfer**: tracking quality, latency, passthrough, room alignment.
