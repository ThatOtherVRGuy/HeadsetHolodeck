# Friday device session — 2026-11-06

One hardware window, so it has to be spent on things only a headset can answer. Everything that the
emulator can cover is being done before Friday and is out of scope here.

## What only the headset can settle

| # | Question | Why the emulator cannot answer it |
|---|---|---|
| 1 | Does gaze actually hover and select a panel tab? | The emulator supplies no `gazePosition`/`gazeRotation`, only a static centre-eye pose with identity rotation |
| 2 | Does the wake word work well enough to ship, and at what rate? | The emulator microphone is silent |
| 3 | Does push-to-talk transcribe a real utterance end to end? | Same |
| 4 | Is the camera preview upright? | Needs a real camera and `WebCamTexture.videoRotationAngle` |
| 5 | Do saves survive a real force-stop and relaunch? | Emulator storage differs; this was verified on an older build |

Everything else — panel navigation, debug command channel, volume, world load and save flows — is being
covered by Codex on the emulator this week.

## Before the session (I will have these ready)

- APK built from the then-current `main`, with its SHA-256 recorded in the release draft
- A fixture set of recorded utterances if the voice recordings arrive; otherwise a scripted list to read
- The exact capture commands below, so nothing is improvised in the window

## Run order

The order matters: if time runs out, the top items are the ones that would otherwise block the release.

| Time | Step | Capture |
|---|---|---|
| 0:00 | Install: `adb install -r <apk>`, push a device `.env`, launch | `[Diag]` line showing `state`, `world`, `hands`, `pointer(uiToolkit=true)` |
| 0:10 | Basics: look around, confirm 90 FPS, controllers tracked | One `[Diag]` line with `fps=` and `controllers=` |
| 0:15 | **Gaze**: `Eye Gesture Mode` equivalent on device — look at the `OBJECTS` tab without moving the head | `[EyeGaze] using gazePosition/gazeRotation … euler=(…)`; does the tab highlight? |
| 0:25 | Gaze select: dwell or pinch while looking at a tab | Panel changes; screenshot |
| 0:30 | **Wake word**: say "computer" 20 times, spaced, no other words | Log lines `[Transcribe] local='…' decision=…`; count matches |
| 0:45 | Wake + command in one breath: "computer, load martian outpost" ×5 | Whether the command survives the wake word in the same utterance |
| 0:55 | **Push-to-talk**: hold, speak "create a world of a castle at sunset", release | `[Transcribe]` line with a sensible transcript |
| 1:05 | **Camera**: open camera, look at a bright scene | Is the preview upright? Screenshot |
| 1:15 | **Saves**: "save this world", force-stop, relaunch, "my worlds", load it back | `[WorldConfigStore]` lines across both launches |
| 1:25 | Spare: any surprise from the emulator work worth a second look | — |

## Capture commands

```powershell
# Full log for the session, with the tags that matter
adb logcat -v time -s Unity > friday-device-log.txt

# The periodic health line, if the above is too noisy
adb logcat -v time -s Unity | Select-String '\[Diag\]'
```

Evidence to keep: `friday-device-log.txt`, screenshots of the camera preview and any panel that changed,
and the wake-word tally (matches out of 20).

## Ground rules for the window

- **Verification only.** No new features, no speculative fixes. If something is broken, note it and keep
  moving; a fix needs a rebuild that costs 20+ minutes of the window.
- **One thing at a time.** Change one variable (mode, command, input) between captures so a log line can
  be attributed to a cause.
- **Say the number, not the impression.** "Computer matched 6 of 20" is useful; "wake word felt flaky" is not.

## What each outcome changes

| Result | Consequence for the release |
|---|---|
| Gaze works | Criterion met; remove it from known issues, state the device evidence |
| Gaze does not work | Ship with controllers and hands as the interaction paths, and say so plainly |
| Wake word ≥ ~70% | Keep it as the headline input |
| Wake word < ~50% | Make push-to-talk the documented primary input and demote the wake word to experimental |
| Camera upside down | One-line rotation fix plus a rebuild; must be caught before the final build |
| Saves fail on device | Release blocker — the save journal is the core of "come back to it later" |
