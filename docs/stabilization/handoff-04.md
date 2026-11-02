# Work order 04 — Define the build credential boundary

Completed 2026-09-13 (America/Los_Angeles) in the current local checkout. Scope: order 04 only.
No commit, push, stash, reset, scene change, or later work order. No APK was produced or published,
no paid generation was invoked, and no real secret value was read, logged, or written to evidence.

The earlier "order 04" task text described the save-write protection that this plan lists as order
03; that work was already complete and was verified separately in
[handoff 03 re-verification](handoff-03-reverification.md). The plan's order 04 — the credential
boundary — is what this document covers, after the user confirmed the plan's numbering.

## Outcome

`BuildEnvCopier` used to copy the entire project-root `.env` into every build, so any APK handed to
someone else carried the developer's OpenAI, World Labs, image-search, audio-search, and
object-generation keys plus anything else the developer kept in that file. A build can no longer do
that by accident: **intent is declared, and a build with no declared intent refuses to embed
credentials rather than guessing.**

## The policy decision order 04 asked for

| Declared intent | What the build embeds | How it is declared |
|---|---|---|
| **Private** — a build for the developer's own machine | Every `.env` entry, byte for byte (today's behavior) | `Headset Holodeck > Build Credentials > Private`, `-credentialMode private`, or `HOLODECK_CREDENTIAL_MODE=private` |
| **Distribution** — a build handed to anyone else | Only non-secret configuration: the `*_BASE_URL` / `*_SEARCH_URL` allowlist. Credentials and unrelated entries are dropped, and the excluded key **names** are listed in the build log | `... > Distribution`, `-credentialMode distribution`, or `HOLODECK_CREDENTIAL_MODE=distribution` |
| **Not declared** | Nothing. If a credential-bearing `.env` is present the build **stops** with an actionable error; if there is nothing to leak it proceeds without embedding | The state until someone picks one of the above |

Resolution order is command line, then environment variable, then the saved per-machine choice.

**Why fail closed rather than silently defaulting either way.** Defaulting to *private* would keep
shipping the developer's keys whenever someone forgot to choose, which is the finding this order
exists to close. Defaulting to *distribution* would equally silently produce a keyless build that
breaks a developer's own Quest testing. Stopping is the only default that is neither a leak nor a
silent break, and the message names both options and exactly how to set each. The friction is one
decision per machine, remembered afterwards; it is loud by construction.

## Rules the boundary enforces

1. A key whose name looks like a credential (`KEY`, `TOKEN`, `SECRET`, `PASSWORD`, `CREDENTIAL`,
   `AUTH`) is **never** embedded in a distribution build — even if someone later adds that name to
   the allowlist. The check is deliberately broad, so a false positive costs one excluded entry.
2. Anything else must be named in the explicit allowlist, so unrelated entries the developer happens
   to keep in `.env` (personal paths, cloud profiles, unrelated tokens) do not travel with the
   artifact.
3. Only key names are ever logged. Values never reach the log, the console, or evidence.

## Failure reporting and cleanup

- Every stage that can fail reports what it was doing plus the cause, and states that the build
  embedded nothing. An undeclared build with credentials fails before any file is written, so a
  stopped build leaves no trace.
- The embedded copy is written atomically: content lands on a hidden `.<name>.<guid>.tmp` sibling
  (hidden so Unity does not import it and mint a `.meta`) and is renamed over the target. A crash
  mid-write cannot leave a partial document that a later build would embed.
- The copy is removed after the build, when the editor quits, and **at editor startup** — that last
  one is new and covers a build that crashed before its postprocess step ran. A distribution build
  additionally removes any stale copy *before* it decides what to embed, so a leftover from an
  earlier private build cannot ride along.
- `.gitignore` now ignores `Assets/Resources/WorldLabs/` as a second net, so a crashed build's
  leftover cannot be committed.
- `CommandLineAndroidBuild` accepts `-credentialMode` and rejects an unknown value before building,
  and `Headset Holodeck > Build Credentials > Show Build Credential Status` prints the intent in
  force and where it came from.

## Files changed

| File | Change |
|---|---|
| `Packages/.../Editor/WorldLabs/BuildEnvCredentials.cs` (+`.meta`) | New: the policy. Mode parsing/resolution, allowlist, credential-shape rule, `.env` parse/filter, atomic embed, removal. No Unity build callbacks, so fixtures drive every branch |
| `Packages/.../Editor/WorldLabs/BuildCredentialSettings.cs` (+`.meta`) | New: the remembered per-machine, per-project choice (EditorPrefs) and the human-readable description |
| `Packages/.../Editor/WorldLabs/BuildEnvCopier.cs` | Rewritten to resolve intent, embed through the policy, log excluded key names, warn on private builds, and clean stale copies at startup |
| `Assets/App/Editor/BuildCredentialModeMenu.cs` (+`.meta`) | New: `Headset Holodeck > Build Credentials` menu (Private / Distribution / Clear / Status) |
| `Assets/App/Editor/CommandLineAndroidBuild.cs` | New `-credentialMode` argument, validated, exported to the build process |
| `Assets/App/Editor/Tests/BuildEnvCredentialsTests.{cs,asmdef}` (+`.meta`) | New EditMode suite, 22 fixtures |
| `.env.example` | Documents the mode and marks the entries a distribution build may embed |
| `README.md` | New "Build Credential Boundary" section |
| `.gitignore` | Ignores the transient embedded copy |
| `docs/stabilization/handoff-04.md`, `STATUS.md`, `evidence-04/*` | This record |

`docs/stabilization/handoff-04.md` from the previous task was renamed to
[handoff-03-reverification.md](handoff-03-reverification.md) with its evidence moved to
`evidence-03-reverification/`, so `handoff-04` is the credential order as the plan intends.

## Checks actually run

Unity 6000.2.10f1, macOS, batch mode, isolated copy, exit 0 for every final check. The user's editor
was closed (no `Temp/UnityLockfile`), so no failure-exiting batch test ran interactively. Provider
networking and reads of the real `.env` were denied
([profile](evidence-04/offline-tests.sb.txt)); credential-looking environment variables were
stripped from the child environment. Every fixture writes its own throwaway files under the process
temp path with obviously fake values.

| Check | Result | Exit | Evidence |
|---|---|---|---|
| Credential boundary EditMode suite | **22/22 passed**, 0 failed/skipped | 0 | [XML](evidence-04/final-cred-tests-01-tests.xml), [log](evidence-04/final-cred-tests-01.log.txt) |
| End-to-end build-callback fixture (new) | **18/18 checks passed** | 0 | [Results](evidence-04/final-cred-e2e-01-results.txt), [probe](evidence-04/credentials-e2e-probe.cs.txt) |
| Save EditMode suite (order 03) | **32/32 passed** | 0 | [XML](evidence-04/final-save-tests-01-tests.xml) |
| Existing batch entry points | **19/19 passed** | 0 | [Results](evidence-04/final-regressions-01-results.txt) |
| Manager transitions (order 02) | **14/14 passed** | 0 | [Results](evidence-04/final-manager-01-results.txt) |
| Local/floor loader transitions (order 02) | **9/9 passed** | 0 | [Results](evidence-04/final-loader-01-results.txt) |
| Restore/autosave transitions (order 02) | **9/9 passed** | 0 | [Results](evidence-04/final-restore-01-results.txt) |
| Save risk probes (order 03) | both green, no risky signals | 0 | [13](evidence-04/final-risk03-01-results.txt), [04](evidence-04/final-risk04-01-results.txt) |

No `error CS` in any final log. [Summary](evidence-04/verification-summary.json),
[runner](evidence-04/runner.py.txt), [driver](evidence-04/run_all.py.txt),
[policy fixture source](evidence-04/credentials-e2e-probe.cs.txt).

The EditMode suite covers mode resolution precedence, unknown-value rejection, command-line
parsing, the allowlist invariant, credential-shape detection, private verbatim embedding, no-`.env`
handling, distribution exclusion of credentials and unrelated entries, proof that an excluded
value never appears in the output, stale-copy removal, nothing-embeddable handling, fail-closed
behaviour, actionable error text, runtime `.env` format round-trip, no leftover temporaries,
replacement of an existing document, and removal of the copy plus its `.meta`.

The end-to-end fixture drives the **real** `BuildEnvCopier` callbacks (through reflection, in an
isolated copy) with a throwaway `.env` of fake values:

```
PASS distribution-keeps-safe-configuration   PASS distribution-drops-credentials
PASS distribution-drops-unrelated-entries    PASS undeclared-build-stops :: BuildCredentialModeException
PASS undeclared-build-says-how-to-choose     PASS undeclared-build-embeds-nothing
PASS private-embeds-credentials              PASS private-leaves-no-temporary-file
PASS postprocess-removes-embedded-file       PASS postprocess-removes-embedded-meta
PASS stale-copy-credentials-do-not-survive   PASS cleanup-removes-everything
PASS cleanup-left-no-stray-resources-dir     CREDENTIALS COMPLETE
```

## Credential hygiene finding found while verifying

The isolated copy at `/private/tmp/holodeck-saves-03-zusaQa/project` still contained a **verbatim
copy of the developer's project-root `.env`** (859 bytes, same size and timestamp as the real file),
left there by the isolation step of the earlier task. It sat outside the home tree, so the evidence
profile's `/Users`-scoped read deny did not cover it. The new fixture refused to run rather than
overwrite it, which is how it surfaced.

Actions taken: the copy was deleted (its contents were never read, printed, or copied into
evidence), the one-byte sandbox probe file was deleted, no `.env` now exists anywhere under
`/private/tmp` (`find` returns none), and the sync step was hardened to delete any `.env` or
embedded `worldlabs_env.txt` found inside an isolated copy and record that it did so. The real
`.env` in the checkout is untouched.

## Evidence profile change

The credential fixtures must create files literally named `.env` and `worldlabs_env.txt` under the
temp path, which the previous profile blocked by name (it also blocked .NET's share-mode open for
writing them, producing `UnauthorizedAccessException`). The deny is now scoped to the home tree and
to `file-read-data`, and each rule uses a single pattern because **the sandbox language ORs the
filters inside one rule** — a two-filter rule denies all of `/Users`, which stops Unity from
starting at all. Verified behaviour: the real `.env` is unreadable, the temp fixture is writable,
and Unity's own support directory is readable. The earlier suites were re-run under the corrected
profile, so every result above comes from one consistent policy.

## Preservation

- HEAD is still `05a09dd46710d82ac4bf1c91c1488d249872ae3d`. No stash was created; nothing was
  committed or pushed; the checkout was never reset or replaced.
- Against the pre-task snapshot of all 4 427 `Assets/`, `Packages/`, `ProjectSettings/` files:
  **2 changed** (`BuildEnvCopier.cs`, `CommandLineAndroidBuild.cs`), **11 added** (the new policy,
  settings, menu, tests and their `.meta` files), **0 removed**, **4 425 untouched**
  ([preservation](evidence-04/preservation-final.json)). The baseline test fixes, order 02 runtime
  changes, order 03 save files, scenes, prefabs, and packages are byte-identical.
- Source versus isolated copy: **match** across all 4 427 files, so the runs validated this exact
  source ([comparison](evidence-04/final-source-comparison.json)).
- `git diff --check` on the changed tracked files is clean; `git check-ignore` confirms the
  transient embedded path is ignored.

## Remaining risks and limitations

- **No APK was built.** Order 04 excludes publishing an APK and a full Android build is ~17 minutes;
  the boundary is verified at the preprocess/embed level plus the printed log summary, not by
  unpacking a real artifact. A future release build should confirm the log line and the absence of
  the embedded copy afterwards.
- **`File.Replace`/rename fallback on Windows and iOS editors is unexercised** (macOS took the
  replace path; the delete-then-move fallback exists for profiles where `File.Replace` is
  unsupported).
- **The saved choice is per machine.** A fresh CI image has no saved choice, so unattended builds
  must pass `-credentialMode` or set `HOLODECK_CREDENTIAL_MODE`; otherwise a credential-bearing
  `.env` stops the build by design.
- **Related paths not covered by this boundary**, unchanged and worth its own order if they matter:
  `HolodeckDirectSecrets.OpenAiApiKey` is a `const string` compiled into the app (currently empty;
  it would embed a key if a developer typed one in), `RuntimeDotEnv` reads `.env` from the device's
  persistent data path (intended, and the way a recipient supplies their own key on Android), and
  `DotEnvLoader` injects `.env` into the editor process only.
- **A distribution build ships without provider keys by design**, so whoever receives it must supply
  their own `.env` or environment variables.

## Next action

Order 05 (Quest acceptance and performance measurement) is the next plan order and does not depend
on this decision. Before sharing any build, run one release-shaped build with intent set to
`distribution` and confirm the log lists the excluded key names and that
`Assets/Resources/WorldLabs/` is absent afterwards. Consider whether the hardcoded
`HolodeckDirectSecrets.OpenAiApiKey` const deserves the same treatment. Nothing has been committed.
