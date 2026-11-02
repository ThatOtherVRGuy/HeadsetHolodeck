# Migrating to the public `HeadsetHolodeck` repository

Goal (owner, 2026-10-29): on Sunday, move this work into a sister repository named **`HeadsetHolodeck`**,
public, "clean", and able to build for **Pico and/or Meta**. This document is the plan and the evidence
behind its constraints. Nothing here is done yet.

## Working model (decided 2026-10-29)

- **All development happens in this repository.** It is the working repo: agents, builds, experiments and
  the messy history live here.
- **The public repo receives major updates only**, as **source-only** snapshots with **no secrets**.
- **The public repo's history is never rewritten or merged into**: each transfer is a normal commit on top
  of whatever is public, and this repo's history is not pushed there.

So Sunday is not a one-time migration but the first run of a repeatable transfer. Section 4 is that
procedure.

---

## 1. History: not a problem for the public repo

The public `HeadsetHolodeck` repository has **never contained a `.env` with real keys** (owner,
2026-10-29), so its history is clean and should be left untouched. Migration is a **source transfer**
into that repo, not a history move.

For the record, this private repository's history does contain keys — `.env` was tracked early and only
untracked later:

| Commit | What happened |
|---|---|
| `8f47cfc` | `.env` added, containing `WORLDLABS_API_KEY` and `OPENAI_API_KEY` |
| `977db71` | `.env` modified |
| `5b69e27` | `.env` modified again ("open ai key to .env file") |
| `4b047d8` | `.env` untracked — **removal does not erase it from history** |

That history stays here. It is a reason **not** to push this repo's history to the public one, and a
reason to rotate both keys if this private repo is ever shared or made public — but it does not affect the
public repo, which never received them.

Practical consequence: transfer the **working tree**, not `git history`. Either copy the tree into a
branch of the public repo and commit it as one change, or copy the files and commit them in logical
groups. Nothing needs rewriting or filtering.

---

## 2. What "clean" should mean

A snapshot of the working tree, with no local residue:

| Exclude | Why |
|---|---|
| `.env` | secrets; `.env.example` stays as the template |
| `Library/`, `Temp/`, `Logs/`, `Builds/`, `obj/`, `UserSettings/` | Unity local state and build output |
| `.pico-cli/`, `.vsconfig`, `*.csproj`, `*.sln` | machine-specific tooling state |
| `Assets/Resources/WorldLabs/worldlabs_env.txt` | a private build can bake keys here |
| Device-only files (`.env` pushes, debug command files) | never part of the repo |
| `Assets/Resources/PerformanceTestRun*.json` | run artifacts, not source |

Also confirm before publishing:

- `.gitignore` actually covers all of the above (it predates some of them).
- No absolute local paths committed (`C:\Users\daren\…`) outside documentation examples.
- Third-party licence files are present for the vendored packages
  (`Packages/com.worldlabs.gaussian-splatting` MIT, `Packages/com.ponyudev.sherpa-onnx` Apache-2.0).
- The debug affordances are off: `DebugCommandIntentBridge.enableInReleaseBuilds = false`.

---

## 3. What "builds for Pico and/or Meta" requires

The switching mechanism already exists and is the thing to carry over, not to reinvent:

- `TargetConfiguration` applies the XR feature set, `applicationEntry`, the manifest template and the
  OpenXR `customLoaderName` per target, and now **fails the build** when a target's features are absent.
- `TargetBuildMenu` builds either target and forces a release build with device credentials.
- `PicoSdkLoaderPrep` renames the PICO loader asset to the name its feature declares; without it the
  Pico build stops in Unity's OpenXR loader step.
- Per-target manifest templates live under `Assets/App/Platforms/{Meta,Pico}/Manifest/`.

Two known prerequisites for a fresh clone to build:

1. **Enable the PICO feature group once** in Project Settings → XR Plug-in Management → OpenXR →
   Feature Groups. Toggling cannot create a missing feature, which is exactly what made a build fail
   half-configured on 2026-10-29.
2. **Commit the OpenXR settings asset with both targets' features present.** This repository currently
   carries the Pico state; a clean repo should ship a state where Pico and Meta features both exist
   (disabled as appropriate) so either build works without a manual step.

Verification for the public repo, in order:

1. Meta build succeeds from a fresh clone, with no PICO feature enabled by default.
2. Pico build succeeds after the documented one-time feature-group step (or immediately, if the shipped
   settings already contain the features).
3. Both install and launch on their respective targets.
4. `Headset Holodeck > Validate Install` reports key *presence* only, never values.

---

## 4. The transfer procedure (Sunday, and every major update after)

Run from this repo. The public repo is a plain checkout of `HeadsetHolodeck`.

1. **Snapshot the source.** Copy the working tree into the public checkout, excluding section 2's list.
   `git ls-files` from this repo is the authoritative source list — do not copy untracked files, which is
   how local `.env`, build output and run artifacts would sneak in.
2. **Audit before committing**, in the public checkout:
   - `git status` — nothing unexpected staged.
   - `git ls-files | findstr /i ".env"` — expect only `.env.example`.
   - a scan for `sk-`, `API_KEY=`, and absolute `C:\Users\` paths outside documentation examples.
3. **Verify it builds from the public clone**: Meta build, then Pico build, per section 3's four checks.
   A transfer that cannot build is not a transfer.
4. **Commit and push** to the public repo. Leave its history alone; this is an ordinary commit on top.
5. **Record the transfer** here: date, the dev-repo commit it came from, and anything deliberately not
   transferred.

Access control is the real guarantee, not this checklist: the dev repo holds the keys and the history, and
the public repo never receives either.

Optional, not required by the public repo: rotate `WORLDLABS_API_KEY` and `OPENAI_API_KEY`. They were only
ever in this private repo's history, so nothing public is exposed — but if this repo is ever shared or
made public, rotate first.

## 5. Open items

- **Ship an OpenXR settings state containing both targets' features.** Today the tree carries the Pico
  state, so a fresh public clone needs the one-time PICO feature-group step before its first Pico build.
  Doing this in the dev repo makes every future transfer build-ready; documenting it as a manual step
  means every clone pays it.
- The vendored packages are modified copies of third-party MIT/Apache code. Publishing them is fine, but
  the README should say which versions and what was changed.

## 6. Sharing with PICO today: two options, one decision

The owner is choosing between reusing the existing public `HeadsetHolodeck` repository (which also carries
Meta assets) and creating a new repository for the PICO work. Either is a **source transfer**; nothing about
this decision changes the code.

| | Existing public repo (Meta assets included) | New PICO-only repo |
|---|---|---|
| History | already clean; never held keys | starts empty, first commit is the snapshot |
| Size | same ~551 MB tracked, plus whatever Meta content already exists | ~490 MB if the macOS natives and unused samples are dropped |
| Audience | shows both targets; Meta content is dead weight for a PICO team | focused; less to explain and less to clone |
| Risk | Meta assets may need their own build verification before publishing | no Meta verification needed |
| Effort today | transfer the tree, verify both targets still build | transfer the tree, verify the PICO build only |

Recommendation if the owner wants the lowest risk today: **new PICO-only repository**, because the only
build that has been verified recently is the PICO one; publishing a Meta target that has not been built
since the drive move would be claiming something unverified.

### Whatever is chosen, these hold

1. **Transfer the working tree, never this repository's history.** `git ls-files` is the authoritative
   source list — copying the folder lets `Library/`, `Builds/`, `Logs/`, `.scratch/` and `.env` ride along.
2. **Nothing secret is tracked.** Verified 2026-11-01: no `.env`, no `.mcp.json`, and `git grep` for key
   patterns across tracked files returns nothing. `.mcp.json` carries a bearer token and is now gitignored.
3. **No file exceeds 100 MB**, so Git LFS is not required (largest: an 87 MB fp32 ASR encoder).
4. **Absolute local paths** appear in only two documents, both as command examples, and should be
   neutralised before handing over.
5. **Verify from the public clone**: build the PICO APK, install, and confirm the beacon reports
   `uiToolkit=true`. That single line proves the interaction fix survived the transfer.

### What a partner needs on day one

`docs/xr/pico/SHARE-SNAPSHOT.md` is written for them: what the project is, how to build and run, what is
verified versus not, the known issues, the secret handling, and where to read next.
