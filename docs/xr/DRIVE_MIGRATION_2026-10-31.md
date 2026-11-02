# Drive migration — 2026-10-31

The working copy is now at D:\Projects\Unity\HeadsetHolodeckDev. The owner copied the entire Projects tree; this session updated tooling and checked the copy. C:\Projects has not been deleted.

## Changes

- Updated project path prefixes in .codex/config.toml, .codex/hooks.json and .pico-env.json in the D: copy only. Installed tools under C:\Users and C:\Program Files remain at their existing locations.
- Replaced all eleven C:\Projects Unity Hub registrations with the corresponding existing D: projects. Registry removal does not delete project files.
- Preserved three additional Git objects/capture references written by the active C: chat after the original copy. Git pack contents match.
- Left gameplay, scenes, platform settings, provider configuration and credential values unchanged.

## Verification

- C: and D: started at commit 80d9bc52ef37b4c4a62ab9d538b111955e467e91, with matching working-tree status.
- SHA-256 compared 4,926 files covering tracked/untracked project files and the local environment file: zero missing files and zero content differences before the intentional config edits. Credential contents were not displayed.
- Read-only robocopy comparison of the whole Projects tree: 177,748 source files, 55,937,834,406 bytes, zero failures and zero mismatches. Nine initially reported differences were the three intentional config edits and Git bookkeeping; source-only capture files were subsequently copied. The timestamp-different Git packs have identical contents. This whole-tree check compared metadata, not every file's hash.
- D: Git connectivity check passed (git fsck --connectivity-only --no-dangling).
- Unity CLI project integrity check passed: 4,013 files scanned, zero errors/warnings, checking metadata, duplicate GUIDs, conflict markers and manifest validity.
- No stale C:\Projects paths found in active App code, ProjectSettings, package manifest or the updated Codex/Pico config. The relocation helper and historical documents retain their example/original paths.
- Unity 6000.2.10f1 Android import/compile and build-scene validation passed, process exit 0. Loaded 1 build scene, checked 1,789 objects, zero missing scripts, zero C# compilation errors. Existing source/package warnings remain (obsolete APIs, a hidden inherited member, an async method without await, duplicate Newtonsoft versions, and PICO package metadata/native-plugin warnings). The initially idle Roslyn server spawned by this run was stopped; compilation then proceeded. Log: Logs/drive-migration-validation.log.
- No new APK build or emulator acceptance run was performed during relocation. The previously built APK was copied with the project.

## Remaining manual step before deleting C:\Projects

This chat's Codex project and already-started MCP processes still use the original C: workspace. In the project's menu choose Edit project, attach/select D:\Projects\Unity\HeadsetHolodeckDev, make it primary, and remove the C: folder attachment. Start/continue a chat whose working directory is D: and restart its project MCP connections. Other Codex projects attached beneath C:\Projects also need their folder settings updated.

Official folder-setting documentation: https://learn.chatgpt.com/docs/projects#use-local-projects-for-folders-and-codebases

The computer-use skill prohibits automating the ChatGPT desktop app, and the available Codex project tools do not provide a folder-path setter. Do not change internal app databases as a workaround. C:\Projects remains available until the owner completes the workspace change.

Caches and SDKs in C:\Users (including Gradle, PICO emulator images and Android tooling), plus the installed Unity Editor, were not moved. They are outside the Projects tree and can remain on C:.

## DeepSeek configuration follow-up

DeepSeek Desktop and its engine were not running. Updated C:\Users\daren\.dsh\storages\workspace.json for HeadsetHolodeckDev and CalendarAlarm to their existing D: folders, preserving workspace/session IDs. Updated 17 cwd/workingDirectory fields across nine cached session records. Verified that no other cached session data changed. Profile YAML/package settings contain no C:\Projects references.

Copied all nine compressed session histories into the corresponding --D-Projects-...-- directories and verified identical hashes; originals under --C-Projects-...-- remain available. Historical conversation contents were not rewritten. Provider credentials were not touched. DeepSeek itself was not launched for a live resume test; the next launch should read the updated saved paths.

Backups and audit: Logs/deepseek-drive-migration-backup-* and Logs/deepseek-drive-migration-audit.json (ignored local files).
