# Order 05b — keys live in the headset's `.env`, never in the APK

Date: 2026-09-13 (America/Los_Angeles). Requested directly by the developer ("the private keys
should be in a .env file on the headset, not baked into the apk"). Scope: this change only. No
commit, push, or change to the distribution/private policy from order 04. No paid generation.

Numbering: this is a bounded follow-up to order 04's credential boundary, run between order 05 and
order 06. It does not renumber anything in [WORK-ORDERS.md](WORK-ORDERS.md).

## Outcome

A third build intent, **device**, joins private and distribution:

| Intent | What the artifact carries | Runtime source |
|---|---|---|
| **Device** (`-credentialMode device`) | Nothing — not even the non-secret URL overrides | `Application.persistentDataPath/.env` on the target (headset: `/sdcard/Android/data/com.QLC.HeadsetHolodeck/files/.env`) |
| Private | Every `.env` entry verbatim | embedded copy, or the device file if one exists |
| Distribution | Non-secret `*_BASE_URL` / `*_SEARCH_URL` only | same as private |

Two runtime changes make the device file authoritative rather than incidental:

1. `RuntimeDotEnv` now has `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, so the device `.env`
   is loaded — and exported into the process environment — before any scene component asks for a
   key. Previously it loaded lazily on first use, which made the World Labs key depend on component
   order.
2. `EnvLoader` (the World Labs plugin's reader) now resolves in this order: explicit path →
   **device `.env`** → project-root `.env` (editor/desktop) → **embedded copy** → process
   environment. The embedded copy is deliberately last so a baked private build can never shadow
   the file the headset owns. `EnvLoader.DeviceEnvPath` and `RuntimeDotEnv.ExpectedPersistentPath`
   are asserted to be the same path by a fixture.

Both readers log which file they used, by name and count only — never a value.

## Files changed

| File | Change |
|---|---|
| `Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCredentials.cs` | `BuildCredentialMode.Device`, `DeviceValue`, `ParseMode`/`Embed` handling (removes any stale copy, embeds nothing, excludes every entry), and a device bullet in the undeclared-intent error |
| `Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildCredentialSettings.cs` | Human-readable description for the device intent |
| `Packages/com.worldlabs.gaussian-splatting/Editor/WorldLabs/BuildEnvCopier.cs` | Logs the device decision and names the exact path the app will read |
| `Assets/App/Editor/BuildCredentialModeMenu.cs` | `Headset Holodeck > Build Credentials > Device (reads .env on the headset)` |
| `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/EnvLoader.cs` | New resolution order, device path, `DescribeSourceOrder`, source logging, actionable "no configuration found" message |
| `Assets/App/Command/SpeechIntent/Runtime/RuntimeDotEnv.cs` | Deterministic load before the first scene |
| `Assets/App/Editor/Tests/BuildEnvCredentialsTests.cs` | 6 new fixtures (22 → **28**) |
| `.env.example`, `README.md` | Document the device intent, the `adb push` step, and the precedence |
| `docs/stabilization/evidence-05b/*`, this handoff, `STATUS.md` | Evidence and record |

## Verification actually run

Unity 6000.2.10f1 in the isolated APFS copy (`/private/tmp/holodeck-saves-03-zusaQa/project`), batch
mode, with provider networking and reads of the real `.env` denied by the harness sandbox; fixtures
write their own throwaway files under the process temp path.

| Check | Result | Evidence |
|---|---|---|
| Credential EditMode suite | **28/28 passed**, 0 failed, exit 0, no compiler errors | [summary](evidence-05b/cred-tests-02-summary.json), [XML](evidence-05b/cred-tests-02-tests.xml) |
| Credential end-to-end build-callback fixture (real `BuildEnvCopier`) | **18/18 checks**, `CREDENTIALS COMPLETE`, exit 0 | [results](evidence-05b/cred-e2e-02-results.txt) |
| Existing batch regression entry points | **19/19 passed**, `REGRESSIONS COMPLETE` | [results](evidence-05b/regressions-01-results.txt) |
| Save EditMode suite (order 03) | **32/32 passed**, exit 0 | [summary](evidence-05b/save-01-summary.json) |
| New fixtures specifically | device build writes no embedded file even when `.env` holds credentials; removes a stale copy from a crashed private build; `device` parses/resolves with command-line precedence; the undeclared-intent error names the device option; `EnvLoader` orders the device file before the embedded copy on Android and before the project-root file on desktop; both readers resolve to the same device path; `RuntimeDotEnv` registers a `BeforeSceneLoad` initialiser | same suites |

Note: the batch-regression Unity process stalled in shutdown because the offline sandbox blocks
Unity's cloud-diagnostics endpoint; the results file had already reported all 19 entries passed, and
the process was terminated. Raw logs are retained under `/private/tmp/holodeck-quest-05/`.

### Artifact verification (value-free)

Build: `-credentialMode device`, exit 0, **15 m 04 s**, report size 2,254,622,351 bytes →
`Builds/stabilization-05b-20260913/Holodeck-order05b-device.apk`, 313,580,753 bytes, sha256
`65eefaf5ca9a1d5be2d809339d78f25fc7386731734f2a07d538da5be76d6fc1`.

Both APKs were searched for the 10 credential/URL values in the developer's `.env` (script reads the
values in memory and prints **key names and counts only** — no value ever reached the log or this
document):

| Artifact | Real values found | Detail |
|---|---|---|
| Old private build (`stabilization-05-20260913`) | **10 of 10** | every credential plus both base URLs, from the embedded `.env` |
| New device build (`stabilization-05b-20260913`) | **0 of 10 credentials** | only `HITEM_BASE_URL` and `THREEDAISTUDIO_BASE_URL` appear, and both are compiled code defaults rather than `.env` entries |

The device build also has **0** `<KEY>=` markers (the private build has 12) and only three
token-shaped strings, all inside `global-metadata.dat` and each an artifact of IL2CPP's pooled
string table (`...maroonma` + `sk-cha...`, `sjis` + `sk-SK...`, `WINDOWS` + `WLT-Ap...`), confirmed
by masking each match and printing its context.

### On-device verification

`adb install -r` (update in place, saved worlds preserved), then a cold start with device logging:

```
[RuntimeDotEnv] Loaded 15 variable(s) from /storage/emulated/0/Android/data/com.QLC.HeadsetHolodeck/files/.env
[EnvLoader] Loaded 15 variables from '/storage/emulated/0/Android/data/com.QLC.HeadsetHolodeck/files/.env'.
WorldLabs.API.EnvLoader:TryLoadFromFile(String, Boolean)
WorldLabs.Runtime.WorldLabsWorldManager:ListWorldsAsync(...)
```

No `[WorldLabsClient] No API key found`, no key-missing warnings, and no HTTP/auth errors in the
capture. That is the APK containing nothing reading its keys from the headset's own file, and the
World Labs client starting a live world-list call from it.

The developer then ran the two spoken confirmations with the headset worn
([excerpt](evidence-05b/device-interactive-verification.txt)):

```
[OpenAiSpeechIntentService] Transcript: "Open my world."
[VoiceCommandRouter] HandleResult transcript='Open my world.' intent=LoadWorldConfig execute=True
[OpenAiSpeechIntentService] Transcript: "Load YoYo Loose Splat Test."
[WorldConfigRestorer] Restoring 'Yoyo Loose Splat Test'
[LocalRemoteSplatLoader] Loaded 'Yoyo Loose Splat Test' (.spz)
[WorldConfigRestorer] Restore complete for 'Yoyo Loose Splat Test'
```

The transcripts themselves came from the OpenAI API, so **OPENAI_API_KEY resolved from the device
.env**, and the cached world restored in 903 ms. The key in the headset's own file was also checked
directly against the provider: a one-item `worlds:list` request returned **HTTP 200 with one world
and a next-page token** ([result](evidence-05b/device-env-key-api-check.txt)) — status and counts
only, key value never printed. No `401`/`403`, no exception, anywhere in the run.

**Order 05b is verified end to end: the artifact carries no keys, and every provider the app uses
resolves them from the `.env` on the headset.**

## Preservation

### Historical key-bearing artifacts removed

Before this change every build embedded the project `.env`, so the machine was carrying eight
key-bearing APKs. After the device build was verified end to end, the developer asked for them to be
deleted. Scanning each artifact for the real values (names and counts only) found:

| Artifact | Values found | Action |
|---|---|---|
| `Builds/Holodeck.apk` (Aug 21) | all 10 | **deleted** |
| `Builds/stabilization-01-20260908/Holodeck-baseline-01.apk` (order 01) | all 10 | **deleted** |
| `Builds/Holodeck-CodexBuildCheck.apk` | all 10 | **deleted** |
| `Builds/Holodeck-CodexTest.apk` | all 10 | **deleted** |
| `Builds/Holodeck-XRITK350.apk` | all 10 | **deleted** |
| `Builds/Holodeck-main-xritk350.apk` | all 10 | **deleted** |
| `Builds/HolodeckPico.apk` (Apr 12) | OpenAI + World Labs | **deleted** |
| `Builds/stabilization-05b-20260913/Holodeck-order05b-device.apk` | none (two code-default URLs only) | kept |

About 2 GB was freed (16 GB → 18 GB free). The sibling `*_BackUpThisFolder…` and
`*_BurstDebugInformation…` folders were scanned individually and contain no credential values, so
they were left in place. A follow-up sweep of the whole working tree (5,295 files outside
`Library/`, `Builds/`, `.git/`, `Temp/`) and of the `Library/` cache (83,282 files) found the real
values **only in `.env`** plus the two public base URLs that are also code defaults.

Recommended follow-up, outside this repository's control: **rotate** the OpenAI, World Labs,
Pixabay, Freesound, xeno-canto, Meshy, Tripo, 3D AI Studio and HiTem credentials, since they lived
inside build artifacts that could have been copied.

- HEAD unchanged; no commit, push, stash or reset. `git status --porcelain` went from 110 entries to
  **112**, and the two additions are exactly the two runtime files this change edits
  (`Assets/App/Command/SpeechIntent/Runtime/RuntimeDotEnv.cs` and
  `Packages/com.worldlabs.gaussian-splatting/Runtime/WorldLabs/EnvLoader.cs`) — both were previously
  untouched by the dirty tree. Every other entry is unchanged.
- The build touched only modification times: a checksum comparison of all `ProjectSettings/` files
  against the pre-change isolated copy shows timestamp-only differences (`GraphicsSettings.asset`,
  `ProjectSettings.asset`), no content change.
- `Assets/XR/Temp/` and `Assets/XR/Temp.meta` are **pre-existing** untracked build-hook artefacts
  (they appear in the order 01 baseline inventory); a cleanup attempt was correctly refused and they
  were left exactly as found.
- The order 04 boundary is intact and additive: private and distribution behave exactly as before,
  and the undeclared case still stops rather than embedding credentials.
- The real project-root `.env` was read only by the two verification scripts, in memory; no value
  was printed, logged, copied into evidence, or written into the repository.
- The headset's existing `.env` was used as-is; this change writes no credential anywhere.

## Remaining notes and limits

- A device build still depends on someone placing `.env` on each headset. If it is missing, the app
  now logs the exact expected path and the provider-specific messages already name it
  (`RuntimeDotEnv.ExpectedPersistentPath`).
- `HolodeckDirectSecrets.OpenAiApiKey` remains an empty `const string`; it would bake a key if a
  developer typed one in. It is unused today.
- The headset `.env` is a plain file readable over adb (mode 644 where adb pushes it). That is
  strictly better than a key-bearing APK, but not a hardware-backed store; a KeyStore/encrypted
  preferences follow-up would be the next step if the keys matter more later.
- `private` remains available as an escape hatch and still produces a key-bearing artifact; it is
  now clearly labelled in the menu and the build log.

## Next action

Use `-credentialMode device` (or the menu's Device entry) for headset builds from now on; keep
`.env` on each headset with the `adb push` line in the README. The key-bearing private APK at
`Builds/stabilization-05-20260913/Holodeck-order05-private.apk` has been deleted (it can be
recreated with `-credentialMode private` if it is ever wanted). Next plan order: 06, the
documentation refresh. Nothing has been committed.
