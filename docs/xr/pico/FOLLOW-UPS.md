# Pico follow-ups (after the first ship)

Owner-requested items, 2026-10-27. Not started; listed so they survive the deadline rush.

## Before shipping: disable the debug command channel

`SpeechIntent.DebugCommandIntentBridge` lets anything with adb drive the app by dropping a command in
`files/debug_command.txt`, and it installs itself at runtime in every build (including release). That is
exactly what was needed to test without a working emulator microphone, and exactly what must not survive
into a build a user holds.

For the final artifact:

1. Set `DebugCommandIntentBridge.enableInReleaseBuilds` to `false` (the component still respects
   `Development` builds, so editor and debug builds keep working), or delete the `AutoInstall` attribute.
2. Remove `files/debug_command.txt` from any test device image you hand over.
3. Re-check that the build used `-credentialMode device` so no keys are embedded, and that the device
   `.env` is the only key source.
4. Confirm the wake-word aliases and the PTT binding are still what the demo expects, since those are the
   user-facing paths once the debug channel is gone.

## 0. RESOLVED — the 3dAIStudio (Tripo) key had expired

Kept as a record of the failure mode, because it is easy to mistake for an app bug.

A text-to-3D request parsed and dispatched correctly, then failed at the provider's balance pre-check:

```
[ObjectGenerationService] 3dAIStudio balance check failed: HTTP 401. API key has expired.
```

**Resolved 2026-10-28:** the owner renewed `THREEDAISTUDIO_API_KEY` and the device `.env` was re-pushed.
A text-to-3D generation then completed end to end, and on 2026-10-29 the 3.2 request path, `face_limit`
and cached-object reuse were all verified.

Still worth a ticket: `ObjectGenerationService.ResolveProvider` treats a provider as "usable" when it is
*configured*, not when its key is *valid*, and it does not fall back after an authentication failure.
Hitem could not cover that case anyway — it is image-to-3D only, and the request was text-to-3D. A second
immediate consequence: an exhausted balance is indistinguishable from a bad key at the UI level until the
preflight message is read.

## 0b. The scene has two WorldConfigStore components

`Assets/Scenes/Holodeck.unity` contains two instances of `WorldConfigStore` (around lines 8759 and
23430, likely one under `Systems` and one under `SpeechIntent`). They keep separate in-memory config
lists over the same directory, which surfaced as a real failure:

```
[WorldViewCaptureService] Panorama capture failed:
[WorldConfigStore] Config 'Holodeck_2026_10_28_17_27_…' is not tracked. Use CreateConfig first.
```

The dispatcher created the config on one store; the capture service asked the other to save it. A
mitigation is in place — `SaveConfig` adopts a config whose folder already exists instead of refusing —
but the underlying duplication is wrong and worth fixing after the ship: one store, injected
everywhere, so a create in one place is visible in every panel and writer.

Note this also means a world saved by one store may not appear in a list read from the other until the
next scan, which is a plausible source of "saved it but the list did not change" reports.

## 1. Loading content from a URL

**Why:** the FILES/URL panel exists but has never been exercised on Pico, and the panel is
world-space UI that the emulator cannot drive. Voice may be the practical path.

**What exists now:** `LocalTypedIntentParser.TryParseContentPath` accepts `http(s)://`, `file://` and
`/`-rooted paths, and `LocalRemoteSplatLoader` / `LocalRemotePanoLoader` handle the fetch. A bare
filename ("load yoyo.spz") is deliberately not treated as a path — recorded as finding F1 in
[handoff 05](../../../docs/stabilization/handoff-05.md).

**Test on hardware:** say `load https://<short-url>.spz` (and a panorama URL). Check: fetch progress,
failure message on a bad URL, unload/cleanup, and that the URL path never falls through to world
generation. Prefer a short URL — ASR over a long URL is unreliable.

**Watch for:** the async fetch running through a transition operation that a later command invalidates.

## 2. PICO's native gaussian-splat renderer

**Claim to verify:** PICO ships a native g-splat renderer that may beat the vendored World Labs
`GaussianSplatURPFeature` we use today.

**What we depend on now:** `com.worldlabs.gaussian-splatting` 1.1.0 (vendored, modified) provides the
API client, runtime splat load (SPZ/PLY), the URP render feature, and the credential boundary.
`RuntimeSplatFloorLoader` and `SplatSpawnEstimator` compute floor/spawn from the splat, and
`WorldConfigAutoSave` caches SPZ files that the restorer re-imports.

**Evaluate:** availability and licence in PICO SDK 6.1.1 / Spatial SDK; point-count and quality at
equal memory; frame time on Swan vs the current renderer; whether it accepts our cached SPZ; whether it
composes with MultiPass and the existing URP feature; and what it would cost to swap given the floor
analysis and save/restore dependencies. Do not replace the vendored package wholesale — it carries the
credential boundary and rendering fixes (see [PICO_SWAN.md](../../PICO_SWAN.md) §5).

## 3. On-device TTS and STT

**Claim to verify:** PICO's on-device speech may outperform the current stack.

**What we use now:** Sherpa-ONNX (PonyuDev, vendored) for VAD + offline ASR wake detection and Piper
TTS, plus OpenAI for command transcription/intent. Measured on the emulator: VAD segments arrive
correctly, ASR decodes on-device, but wake-word recognition is fragile ("computer" came through as
"THE WORLD", "Serene" as "SERIOUS") — the same failure class recorded on Quest.

**Evaluate:** recognition accuracy on the wake alias set and on commands; latency for
wake→transcript; offline behaviour; whether it can replace `whisper-1`/`gpt-4o` for command intent
(cost and privacy win); TTS voice quality and latency vs Piper Kristin; and API/permission surface.
Measure with the same phrases used tonight so the comparison is meaningful.

### Why this moved up the list (2026-10-27, emulator)

Wake-word detection was the dominant failure of the session. Every rejection is logged, and the
observed mis-hearings are the documented Quest set: `computer` → `LETTER` (twice), `THE WORLD`,
`AND ONCE`; `Serene` → `SERIOUS`. Push-to-talk (Space) works reliably, which is why the owner uses it.
**This is a recognition problem, not a matching problem** — the config already accepts six aliases
with `StartsWith` matching at a 0.171 floor, and adding more near-misses to that list is treating the
symptom. (Adding `letter` is tempting and wrong: it would fire on ordinary speech.)

### Candidate: Sherpa-ONNX keyword spotting (cheapest, most likely win)

`KwsWakeTrigger` is already a placeholder for exactly this, and the vendored Sherpa-ONNX package
supports a purpose-built KeywordSpotter — a small KWS model watching for one phrase, instead of
running full ASR over every segment and text-matching the result. Same runtime, same native libraries,
no new licence, and it removes the "ASR must spell my wake word" failure mode. Model choice and the
false-accept rate on ordinary conversation are the things to measure.

### Candidate: PICO's on-device STT/KWS

Check what PICO OS 6 exposes natively (wake-word service, on-device ASR, on-device TTS) and whether it
is available to a Unity OpenXR app without the Spatial SDK. If it exists, it should be compared against
Sherpa KWS on the same phrases and the same device.

### Instrumentation added (2026-10-27)

`VadAsrWakeTrigger` now logs one `[WakeDiag]` line per evaluated phrase:

```
[WakeDiag] heard=' LETTER' normalized='letter' nearest='computer' similarity=0.21
           matched=False wake='' mode=StartsWith aliases=computer/pewter/pudor/peter/impewter/pumpor
```

That gives a measurable record — which phrases are heard, how far they land from the nearest alias,
and how often the match succeeds — so the choice between "widen the alias set", "switch to KWS" and
"switch to PICO native" can be made from data after a normal session rather than from tonight's
anecdotes.


## Tomorrow's agenda (2026-10-28)

### A. Hand pointing and the world-space UI

The LCARS panel is world-space UI driven by XR rays; Android touch does not reach it, and no
controllers are paired on the emulator, so **interaction is the largest untested area**.

1. First check whether the PICO emulator can simulate controllers or hands at all — its settings
   window has a controller page, and the CLI exposes `--args`. If it can map the mouse to a controller
   ray, that unlocks UI testing without hardware.
2. If it cannot, validate the pointing *logic* in the editor instead: `HolodeckRigTestHarness` poses a
   semantic rig for gaze/hand pointing, and `XRHandsGestureSource` / `HandPointingSourceDriver` /
   `HandOnlyInputCoordinator` can be driven through it. That proves targeting and hover/selection
   plumbing; it does not prove tracking quality.
3. On hardware, the checklist rows 4 and 5 cover pairing, pointer ray, hover, select, pinch and grip.

### B. Loaded sounds

Evidence already exists that the pipeline runs on Pico (21:36 session):

```
[AudioWorldActionController] Searching freesound for 'local_sereneJapanesegarden_…'
[AudioWorldActionController] Downloading 'Binaural F# - 23.15HZ F#0 & 46.31 F#1' from freesound…
[AudioWorldActionController] Playing 'Binaural F# - 23.15HZ F#0 & 46.31 F#1'.
```

What is **not** yet proven is the part that matters:

1. **Audibility** — does the clip actually reach the headset/speaker path (the emulator routes host
   audio, so this is partly an emulator question; confirm on hardware).
2. **Persistence** — place a sound, `save this world`, force-stop, relaunch, `load <world>`, and check
   that `[WorldConfigRestorer]` re-creates the audio object and that playback resumes.
3. **World-load sounds** — whether a world's ambient/auto audio still starts after a cached restore
   (the SPZ is cached, so the load must not skip its audio step).
4. **Volume and placement** — the prior Quest finding was that an audio object persisted but was not
   heard after restart; make sure Pico does not repeat that.

Search keys are present in the device `.env` (`FREESOUND_API_KEY`, `XENO_CANTO_API_KEY`); the three
`*_SEARCH_URL` overrides are intentionally empty.

Search keys are present in the device `.env` (`FREESOUND_API_KEY`, `XENO_CANTO_API_KEY`); the three
`*_SEARCH_URL` overrides are intentionally empty.

## Smaller items already identified

- **Incomplete worlds are listed like real ones.** A cancelled or unfinished generation leaves a
  config with a `world_id` but no splat, no objects and no spawns, and it appears in My Worlds — and
  with newest-first sorting it appears at the *top*. Owning decision (2026-10-28): leave the specimen
  on the device for now (`purple_elephant_2026-10-28T074144Z_05d639`) rather than deleting it. Before
  shipping, either exclude worlds without content, label them "generation incomplete", or delete the
  stub on cancel. Cancel is also local-only: the server-side world may still exist, so cancellation
  should be checked against the account rather than assumed.
- **`[WorldLabs] Poll error: code=, message=''` is a logging bug, not a failure.** The poll returns
  HTTP 200 with an empty `error` object; the coordinator logs any non-null error as a failure, so a
  healthy multi-minute generation looks broken. Treat empty code+message as no error and surface
  `metadata.progress` instead.
- **World sort default is implemented** (`c9b5ffb`): newest-modified first, with
  `WorldSortSettings` exposing field, direction, `Cycle()` and `Describe()` for a control surface.
  Still missing: the voice ("sort worlds by name", "newest first") or panel control that uses it.

- ~~Trim trailing punctuation from the spoken world name in the "Shall I create it?" question
  ("purple elephant..").~~ Done in `f56acfe`.
- Close the asynchronous load path: if the startup config scan has not finished, a miss still only
  warns instead of offering creation.
- Restore Meta parity for the target switch: move `PICO_SPATIALIZER`,
  `ENABLE_PICO_OPENXR_SDK`, `ENABLE_PICO_INTERACTION_PACK` from global Android defines into the Pico
  Build Profile, because the PICO SDK's validators run on every Android build and reject the Quest
  target's GameActivity.
