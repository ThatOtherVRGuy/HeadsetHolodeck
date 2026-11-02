# Release plan — final PICO release in two weeks

Draft started 2026-11-01. Target: a published, non-draft release by **2026-11-15**.

**This is an emulator release.** The PICO headset is not publicly available yet, so the deliverable is a
build validated against the PICO emulator, with the Meta Quest path remaining the way to run the
experience on real hardware. Device criteria below apply to Quest; PICO criteria are emulator-scoped. Do
not describe an emulator pass as a device pass anywhere in the release notes.

The draft release `pico-2026-11-01` already exists with an APK attached, and it is the fallback: if
everything after this point slips, there is still a tagged artifact to hand over. Everything below makes
that artifact trustworthy rather than merely present.

## What "done" means

A final release must satisfy all of these. Anything not met is either fixed or written into the release
notes as a known limitation — silence is not an option.

| # | Criterion | Evidence required |
|---|---|---|
| 1 | Cold clone builds an APK with no manual steps | Build from a fresh clone, APK installed and launched |
| 2 | Controller ray hover and UI select work | Screenshot or beacon from the **emulator** |
| 3 | Push-to-talk works, end to end | Transcript in the log — **emulator microphone is silent, so this needs a headset or the fixture replay** |
| 4 | Speech commands reach the dispatcher offline | `decision=AcceptLocal` lines from the fixture replay |
| 5 | World load, save, and reload survives a restart | `[WorldConfigStore]` lines across two launches |
| 6 | Wake word has a measured recall number | Fixture replay count, stated in the release notes |
| 7 | No keys in the artifact | `-credentialMode device` build; manifest and `Resources/` checked |
| 8 | Known-issues list is current and honest | Reviewed against `SHARE-SNAPSHOT.md` |

Criteria 2, 5 and 7 were already verified on hardware earlier; they need re-verification against the
final build rather than being assumed to still hold.

## Week 1 (2026-11-02 → 08): fix the things that make it unreliable

Ordered by user impact, not by difficulty.

1. **Wake-word recall.** The measured number is 1 in 15 on hardware. The fixture set exists
   (`tools/split_voice_fixtures.py`, 13 groups, 39 utterances) but has not been recorded yet. Record,
   replay, and get a baseline number; then decide between contextual biasing (needs a transducer model
   and `modified_beam_search`) and accepting push-to-talk as the primary input and saying so.
2. **Microphone death after a system dialog.** Any permission prompt or app pause leaves the wake
   trigger dead until relaunch. This is the worst failure mode in a demo: it looks like the app froze.
3. **Gaze on hardware.** Implemented against OpenXR `gazePosition`/`gazeRotation`; the emulator cannot
   exercise it. One hardware session settles it, and it is the only unverified interaction path.
4. **Camera rotation on device.** The rotation fix (`WebCamTexture.videoRotationAngle`) has never run on
   hardware.
5. **Local-first coverage.** Ten everyday commands still need the cloud because the capability does not
   exist: world cycling, help, brightness, and disambiguation for `stop` / `brighter` / `reset the
   world`. Global mute/mute shipped already; these are the remainder.

## Week 2 (2026-11-09 → 15): make the release defensible

6. **Regression matrix on hardware**, one row per capability in `SHARE-SNAPSHOT.md`, with the artifact
   hash recorded beside each result.
7. **Documentation pass.** `SHARE-SNAPSHOT.md` and `USER_MANUAL.md` must describe the shipped build, not
   an earlier one. Confirm the README's first click lands on the truth.
8. **Version bump** to 9.3.0 (from 9.2.1) and a fresh `-credentialMode device` build.
9. **Publish the draft**: clear the draft flag, mark prerelease off, attach the final APK, add a
   "verified on" section listing hardware, OS version and date.

## Risks

- **No hardware right now.** Criteria 2, 3, 5 and the gaze/camera checks all need a headset. If one does
  not appear in week 1, the release cannot honestly claim them — the plan then becomes "publish what is
  verified, list the rest as untested", which is weaker but still honest.
- **Wake word may not be fixable in two weeks.** If the fixtures show the model simply misses the phrase,
  the answer is to make push-to-talk the documented path rather than to ship a headline feature that
  fails 14 times in 15.
- **Emulator ceilings.** Gaze cannot be tested there, the microphone is silent, and synthetic key taps do
  not reach XR input. Do not let emulator passes stand in for hardware evidence in the release notes.

## Division of labour

| Who | Does |
|---|---|
| Owner | Hardware sessions, recording the voice fixtures, final go/no-go |
| Codex session | Emulator interaction passes, beacon and screenshot evidence |
| This session | Code, builds, docs, release mechanics and verification |
