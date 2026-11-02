# Local-first transcription

Design note for the cascade the owner asked for: *"a fairly good local model and external fall-back. If the
local model succeeds, great. If not, send the same audio clip to a transcriber. If that fails for any
reason, respond with an apology."*

Implemented in `LocalFirstTranscriptionPolicy` + `VoiceCommandRouter` (commit `ba9ef2d`).

## Why this was needed

The router already received the on-device transcript as `fallbackText` — it is produced anyway, because
that is how the wake word is detected — but it was **discarded whenever the cloud path was configured**.
So every command cost a network round trip even when the reserved grammar understood it perfectly, and a
device with no connectivity could not act on commands the local grammar would have handled.

## The decision, per utterance

```
clip captured
   ↓
local transcript empty?              → cloud
   ↓
local grammar understands it?        → ACCEPT LOCALLY (no network, no cost, works offline)
   ↓
known junk transcription?            → cloud
   ↓
otherwise                            → cloud (Whisper is better at free-form speech)

cloud fails
   ↓
local transcript plausible?          → use it
   ↓
otherwise                            → speak "Sorry, I didn't understand that."
```

Note the last step: the failure case speaks. Silence is indistinguishable from a frozen app, which is
exactly how the wake-word failures read on hardware.

## The honest limitation: there is no confidence score

Sherpa-ONNX returns token log-probabilities, but the Unity wrapper exposes only text, so there is no
probability to threshold. The gate therefore uses two things that do exist:

1. **The reserved local grammar** (`OpenAiSpeechIntentService.TryBuildLocalPreflightCommand`) — if it
   parses, the command never needed the cloud.
2. **The junk transcriptions actually observed** (`well`, `no`, `what`, `louise`, `dudle`, …), taken from
   [WAKE-WORD-EVIDENCE.md](WAKE-WORD-EVIDENCE.md).

Replacing this with a real threshold needs the wrapper patched to surface mean per-token log-probability.
That is the principled version; this is the version that works today.

## Verified in the editor (2026-10-31)

Run through `script.eval_file` against utterances from the captured logs:

| local transcript | decision | reason |
|---|---|---|
| `""` | UseCloud | local produced nothing |
| `well` / `NO` / `louise` / `dudle` | UseCloud | known junk (usable fallback = false) |
| `Load Elvin City.` | **AcceptLocal** | grammar → `LoadWorldConfig` (trailing period stripped first) |
| `load castle dot glb` | **AcceptLocal** | grammar → `LoadSplat` |
| `show panorama` | **AcceptLocal** | grammar → `ShowPanoWorld` |
| `save this world` | UseCloud | not in the preflight allowlist |
| `create a castle on a snowy mountain at sunset` | UseCloud | free-form |

Every decision logs one line, so the ratio of "never left the device" is measurable:

```
[Transcribe] local='load castle dot glb' bytes=48210 decision=AcceptLocal reason='local grammar understood LoadSplat'
```

## Follow-up worth doing

`save this world` was short, unambiguous, and paid for a cloud call — worse, it was not parsed locally at
all, so it always needed the network. **Done** (commit `629217a`): the parser now handles "save this
world", "save the world" and "save as <name>", and the allowlist accepts `SaveWorldConfig`,
`CaptureWorldThumbnail`, `CaptureWorldPanorama` and `CaptureHeadsetCamera`.

Deliberately still on the cloud path, verified by probe:

| Utterance | Local intent | Why it stays cloud |
|---|---|---|
| `delete this world` | `DeleteTarget` | destructive; a misparse must not delete |
| `add sound of rain` | `CreateAudioSource` | phrasing variety is unbounded |
| `exit holodeck` | `QuitApplication` | a misparse must not quit the app |

## The twelve `Unknown` commands: one parser gap, ten missing features

Probing 24 common utterances showed the local parser returning `Unknown` for twelve everyday phrases. It
would be easy to read that as a parser gap. It is not — only one of them is.

**Fixed (commit after `085d998`):** `my worlds`, `list my worlds`, `list worlds`, `saved worlds`,
`show worlds`, `open worlds`, `my saved worlds`, `show my saved worlds` now resolve to
`LoadWorldConfig` with an empty name, which opens the saved-worlds panel. The capability existed; only
"open my worlds" and "show my worlds" were matched exactly, so the phrasing people actually use fell
through to the model.

**Not parser work — the app has no such capability.** Verified by searching the whole codebase: there is no
global mute, no world cycling, no help panel. Mapping these to an existing intent would make the app do
something unrelated, which is worse than asking the model.

| Utterance | Why it stays `Unknown` | What it would need |
|---|---|---|
| `mute` · `unmute` | `ControlAudioSource` acts on an *existing* audio source; there is no global mute | an audio bus / `AudioListener.volume` intent |
| `volume up` | same, plus an amount | global volume intent with a step |
| `next world` · `previous world` | no world-cycling concept at all; worlds are loaded by name | a "playlist of loaded worlds" feature |
| `show help` · `help` | no help/instructions panel exists | a help panel and a `ShowUi` panel name |
| `stop` | ambiguous: stop audio? stop generation? stop behaviour? | disambiguation via the existing `AskClarification` |
| `brighter` | `ModifyLight` needs a target light | default-light resolution, or clarification |
| `reset the world` | ambiguous: reset transforms? reload the saved world? | clarification, or a distinct `ResetWorld` intent |

The next real win is therefore not a parser change but a small feature: a global volume/mute intent, which
is cheap, unambiguous, and the kind of thing a demo asks for constantly.

## Misparse to keep out of the allowlist

`make it night` parses as **`PlaceObject`**. It currently goes to the cloud, so the bug is masked in
practice — but it is the reason `PlaceObject` must never join the preflight allowlist without fixing the
parser first.


