# Folder migration: Unity validation attempt

Date: 2026-10-26. Unity: 6000.2.10f1. Baseline: c1fe017 plus the uncommitted shared/platform folder migration.

## Final result — passed

After license activation, Unity imported the project and successfully built the Meta Android APK.
The final pass restored the original `UNITY_MCP_READY` / `UNITY_MCP_DEPS_2` scripting flags and
used `Holodeck.Editor.ProjectLoadValidation.ValidateAndBuildApk`.

- Final Unity process exit code: **0**.
- Build result: **Succeeded**, duration **00:11:20.6100445** (incremental final pass).
- Scene validation: **1 scene, 1,789 objects, zero missing script components**.
- APK: `Builds/Meta-folder-validation.apk`, **313,301,651 bytes**.
- SHA-256: `9F67923997912DCE0C3C68384E6D1C1864DADE07B4112F4F8285C5F118A68EFC`.
- Final full log: `Logs/meta-folder-validation-final.log` (generated/ignored).
- First successful build: `Logs/meta-folder-validation-build.log`, exit 0, duration 00:40:38.4668661.
- Existing compiler/deprecation and SDK/tooling warnings remain; this is not a zero-warning claim.
- No headset runtime smoke test was performed.

The initial licensed retry exposed missing generated Unity MCP NuGet dependencies: ready flags were
tracked but `Assets/Plugins/NuGet` is intentionally ignored. Temporarily cleared the two gate flags,
ran the package's own `NuGetResolverMenu.ForceResolve`, then restored the original scripting define
section before the final successful build. ProjectSettings matches the original tracked content.
No package manifest or lockfile change was required. Generated dependencies remain local/ignored.

`ProjectLoadValidation` is a batch-only editor entry point that opens the enabled build scenes,
checks all objects (including inactive objects) for missing scripts, then calls the existing APK
builder. It does not save changes to scenes. Use the original command below with
`-executeMethod Holodeck.Editor.ProjectLoadValidation.ValidateAndBuildApk` for future verification.

## Initial attempt — historical licensing blocker

**Blocked before project compilation by Unity licensing. No successful import, compile, or Android build is claimed.**

Launched the installed Unity Editor with:

```text
-batchmode -nographics -quit
-projectPath <repo>
-buildTarget Android
-executeMethod Holodeck.Editor.CommandLineAndroidBuild.BuildApk
-outputPath Builds/Meta-folder-validation.apk
-credentialMode device
-logFile <repo>/Logs/meta-folder-validation.log
```

Unity started, connected to its license client and Package Manager, and began creating the missing
Library cache. Package downloads from Unity/OpenUPM succeeded. The Editor repeatedly reported:

```text
Found 0 entitlement groups and 0 free entitlements matching requested entitlement ids
Error: 'com.unity.editor.headless' was not found.
```

The log stopped progressing at initial asset refresh before project compilation or the build method.
The validation process was stopped to release its project lock; wrapper exit code was -1 from that
termination. No APK was produced. Package manifest/lockfile and ProjectSettings were unchanged by
the attempt. The local full log is `Logs/meta-folder-validation.log` (generated/ignored).

## Static verification

- All 465 relocated files retain original content except intended path replacements and line endings.
- All 249 original metadata/assembly-definition blobs match at their new locations.
- Hardcoded Unity asset paths in shared C# code resolve for 22 references. The only absent path,
  `Assets/App/Prefabs/TestRig_Me.prefab`, is an intentional output written by
  `HolodeckRigTestHarnessSetup` via `PrefabUtility.SaveAsPrefabAsset`; it is not a lost moved asset.
- No old application/profile paths remain in the active source and updated setup documents.
- `git diff --check` passed.

## Initial retry steps — completed

1. Sign in to Unity Hub and activate the user's available Editor license under Settings → Licenses.
2. Rerun the command above and resolve any actual import/compiler/build errors that appear.
3. Confirm the existing Holodeck scene opens without missing scripts and run the existing editor
   checks relevant to the migration.
4. Require BuildResult.Succeeded and a produced APK before declaring Android build validation passed.

Free space was about 23.4 GB after the initial package/cache activity and about 7.1 GB after the
completed builds. The separate Swan emulator storage requirement remains unresolved.
