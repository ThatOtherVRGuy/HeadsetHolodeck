# Recording voice fixtures (morning routine)

Goal: measure speech recognition against **your** voice, repeatably, without needing a working microphone
at test time. The emulator's microphone is silent, so recorded clips are the only way to exercise the
speech path there.

## 1. See what to record

```powershell
& "C:\Users\daren\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe" `
  tools\split_voice_fixtures.py --checklist
```

It prints the 13 files, the phrase for each, and how many takes. The important one is
`wake-computer.wav` — ten takes of just "computer", because that is the 1-in-15 problem.

**How to record:** one file per row, named exactly as shown, pausing about a second between takes. Start
with a second of silence. Normal speaking voice. Any sample rate; stereo is fine. **Do not normalise or
denoise** — the point is to capture what the headset would actually hear.

## 2. Split the takes into fixtures

```powershell
& "C:\Users\daren\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe" `
  tools\split_voice_fixtures.py --input C:\Users\daren\Music\voicetakes
```

- Finds the pauses, trims each take, resamples to 16 kHz mono, writes `VoiceFixtures\<group>-01.wav` …
- Reports when a group produced more or fewer takes than expected. If it under-splits (two takes merged),
  lower `--min-gap` (e.g. `0.20`); if it splits mid-word, raise it. If it detects nothing, raise the floor:
  `--threshold-db -45`.
- `--push` additionally copies the result to the emulator over adb.

The splitter was tested against a synthetic 48 kHz stereo recording: ten bursts in, ten correctly named
16 kHz clips out.

## 3. Replay them on the device or emulator

```powershell
adb push VoiceFixtures /sdcard/Android/data/com.QLC.HeadsetHolodeckPico/files/
# then, via the debug command channel:
echo replay > debug_command.txt      # runs every clip in the folder
echo replay cmd-my-worlds-02.wav > debug_command.txt   # or one clip
```

Each clip reports one line:

```
[Fixture] wake-computer-01.wav dur=0.84s local='COMPUTER' decision=UseCloud reason='…' grammar=none
[Fixture] cmd-save-this-world-01.wav dur=1.90s local='SAVE THIS WORLD' decision=AcceptLocal reason='local grammar understood SaveWorldConfig' grammar=SaveWorldConfig
[Fixture] done: 34/39 clips produced text.
```

Three numbers of interest:

- **`N/39 clips produced text`** — raw recognition recall
- **how many `wake-computer` clips transcribed as `COMPUTER`** — the wake-word metric, directly comparable
  to the 1/15 measured on hardware
- **`decision=AcceptLocal` on the `cmd-*` groups** — confirms the local-first path is doing its job

## What happens next

Send me the folder path (or just run the split and say it is done). I will replay them, tabulate the
results into `docs/xr/pico/WAKE-WORD-EVIDENCE.md`, and compare model or alias changes against that
baseline. The whole point is that the number stops being an anecdote.

## Files

- `tools/split_voice_fixtures.py` — the splitter (numpy only, stdlib `wave` for I/O)
- `Assets/App/Shared/Command/SpeechIntent/Runtime/VoiceFixtureReplay.cs` — runtime replay, driven by the
  `replay` debug command
