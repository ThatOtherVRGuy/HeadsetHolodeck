# Pico Unity and AI development environment

Checked and configured 2026-10-26 (America/Los_Angeles). Target: Swan / PICO OS 6.

## Readiness verdict

Live recheck 2026-10-27: SDK and MCP probes pass; `emulator-5590` is online. An unchanged Meta APK
installed and launched under ARM64 compatibility, but XR initialization failed because the app
lacks Pico immersive-app marking. See [the focused emulator plan](EMULATOR_PARITY_PLAN.md) for
the current evidence and next work. Approximately 25.8 GiB remained after this diagnostic probe.

Updated 2026-10-27: the Swan emulator boots, Unity 6000.2.10f1 compiles the installed Pico SDK/tooling, and a live relay MCP handshake plus `pico_xr_status` call pass. Initialization is complete in OpenXR mode. App target isolation, a post-integration Meta regression build, and Pico app/device feature acceptance remain pending. Earlier storage/installation findings below are historical.

The existing Meta project independently passed import, compilation, scene loading and Android
build with the original scripting flags restored; see [the validation report](../meta/FOLDER_VALIDATION.md).

## Installed and verified

| Component | Result |
| --- | --- |
| Unity Editor | 6000.2.10f1; installed Android, SDK/NDK and OpenJDK modules verified by Unity CLI |
| Unity license | Working; final Meta batch build exited 0 |
| Android Studio | Installed; JBR reused rather than installing another Java runtime |
| Android SDK | Pico doctor confirms API 35 platform and sources in the user's Android SDK |
| Node/npm | Node 24.13.0, npm 11.6.2 |
| PICO CLI | 0.6.1 public; current according to its version diagnostic |
| Unity CLI | 1.0.0-beta.14; installed and executable verified |
| PICO Unity agent tools | 0.1.1, project-local Codex resources; plugin doctor SUCCESS |
| PICO general agent tools | 0.1.1, project-local Codex resources; plugin doctor SUCCESS |
| Unity SDK knowledge data | 6.1, installed by agent-vault 6.1.2 |
| Knowledge runtime | Graphify 0.6.0; knowledge doctor SUCCESS, graph data and MCP serve probes passed |
| Profiling | PICO performance tool 0.2.1; installed missing Perfetto trace processor v49.0; doctor passes |
| Virtualization | PICO emulator doctor reports Windows virtualization ready |

Pico setup installed 14 local skills, including initialization, package management, XR building
blocks, Spatial Adapter, SpatialML and diagnostics. This prepares developer assistance; it does
not install an on-headset inference service, validate a model pipeline, or enable application features.

Setup command used:

```powershell
pico-cli setup --agent-tool codex --platform unity --region us --scope local --project <repo> --yes
```

The setup preserved the repository's AGENTS guidance and appended managed links to
`PICO-UNITY-AGENTIC-TOOLS.AGENTS.md` and `PICO-GENERAL-AGENTIC-TOOLS.AGENTS.md`.
Skills and host MCP settings live under ignored `.agents/` and `.codex/`; `.pico-env.json` is also
ignored because it records machine-specific paths and installations.

## Repairs performed

- Pico setup's Unity CLI installer invoked Windows PowerShell and failed because `Get-FileHash`
  was unavailable there, yet setup printed an installed message. Downloaded the official installer,
  ran it under PowerShell 7, completed checksum verification, and verified the actual CLI version.
- Installed the missing trace processor with `pico-cli perf doctor install --tool tps`.
- Set user `JAVA_HOME` to the existing Android Studio JBR only because neither user nor machine
  JAVA_HOME was set. Configured user `PICO_HOME` for the installed Pico SDK tools directory.
- New terminal/agent processes must reload the updated user environment and local plugin configuration.

## Remaining gaps

### Storage and emulator

Only about 6–7 GB remained after the Meta builds and development-tool installation. Pico documents
at least 32 GB for emulator/system image/SDK/AVD storage. Free at least another 26 GB, preferably
with additional Unity build headroom, or provide another suitable volume before resuming installation.
No existing user data or build evidence was deleted to make room.

Swan emulator 6.1.0 was selected by the earlier installer but its download was interrupted before
extraction. There is no installed bundle or running AVD. Doctor reports prerequisites ready while
explicitly **skipping the missing emulator**; its SUCCESS is not an emulator smoke-test pass.

### Project SDK and editor AI bridge

The current project manifest contains neither `com.bytedance.pico.xr` nor
`com.bytedance.pico.mcp-extensions` nor `com.unity.ai.assistant`.
The existing Ivan Murzak Unity MCP package is a different integration and does not satisfy the
official Pico extension's Unity AI Assistant dependency.

The Pico setup generated local `pico-dev-knowledge` and `unity-mcp` server entries. The Unity entry
currently targets `C:/Users/daren/.unity/relay/relay_win.exe`, which does **not** exist yet. Installing
Unity CLI alone does not install the project's AI Assistant relay. Knowledge service runtime was
verified independently, but neither service is loaded into this existing chat's tool registry.

Missing live editor capabilities include `pico_xr_package` and `pico_xr_status`. No Pico package
mutation or scene editing was attempted through a partial/missing tool set. Restart Codex after
installation, then verify actual server connections and tool availability, not just configuration.

Pico's current MCP extension declares SDK 6.1.0, Input System 1.18.0 and Unity AI Assistant
2.9.0-pre.2 dependencies. This project uses Input System 1.15.0. Integrating these is a package
compatibility change requiring another Meta regression build and the target-isolation work in
[the shared plan](../PLAN.md). The official resources page currently advertises Swan SDK 6.1.1;
choose a compatible pinned combination during integration rather than equating knowledge-data
version 6.1 with an installed SDK.

The installed Pico guidance makes `/pico-unity-init` an explicitly invoked initialization workflow.
It has not been invoked and `.pico-cli/config.json` has no completion marker. Machine setup is
complete for the installed host tools; project SDK initialization remains a distinct pending step.

### Device and runtime acceptance

No Pico hardware was connected. Microphone behavior, gaze calibration, room capture, passthrough,
performance and the final feature matrix remain unverified. SpatialML/SecureMR guidance is available,
but no device ML runtime or Pipeline Zoo package was installed or tested.

The official MCP extension distinguishes OpenXR from Pico-native support: spatial mesh has an
OpenXR path, while its plane-detection and controller-haptics blocks currently require Pico-native
runtime. Treat those as capability-specific findings to validate, not a reason to fork shared logic.

## Diagnostic evidence and repeat checks

Generated local reports are under `Logs/`:

- `pico-unity-doctor-final.json`: SUCCESS, Unity environment usable; missing emulator skipped.
- `pico-plugin-doctor.json`: SUCCESS, project-local Codex resources current and complete.
- `pico-knowledge-doctor.json`: SUCCESS, graph and MCP serve readiness verified.
- `pico-emulator-doctor-final.json`: prerequisites ready; emulator bundle skipped/absent.
- `unity-installed-editors.json`: installed editor/module inventory.

After opening a new terminal:

```powershell
unity --version
pico-cli doctor --platform unity --agent-tool codex --format json
pico-cli plugin doctor --scope local --project <repo> --agent-tool codex --format json
pico-cli knowledge doctor --format json
pico-cli perf doctor check
pico-cli emulator doctor --format json
```

After resolving storage, resume emulator install/create/start using the official guide, then record
actual boot, device listing, graphics and app launch evidence. After SDK/editor bridge integration,
verify the live tool registry before package or scene operations and repeat the Meta build.

## Official documentation

- [Swan SDK resources and downloads](https://developer.picoxr.com/resources/?platform=unity)
- [Pico Unity SDK documentation](https://developer.picoxr.com/document/unity-swan/)
- [Pico Unity + AI tooling quickstart](https://developer.picoxr.com/document/pico-cli/quickstart-unity/)
- [Pico CLI installation and agent setup](https://developer.picoxr.com/document/pico-cli/quickstart-common/)
- [Emulator lifecycle, requirements and limitations](https://developer.picoxr.com/document/pico-cli/emulator-guide/)
- [Official Pico MCP Extensions](https://github.com/Pico-Developer/Unity-MCP-Extensions)
- [Unity MCP prerequisites and connection](https://unity.com/blog/unity-ai-mcp-how-to-get-started)
- [Pico SecureMR and SpatialML overview](https://developer.picoxr.com/blog/securemr/)

## Installation resumed 2026-10-27

- Owner requested all installation remain on C:; D: is temporary.
- Official global Swan emulator 6.1.0 archive downloaded completely; installer checksum verification passed and installation exited 0.
- Installed bundle: `C:/Users/daren/AppData/Local/PICO/sdk/6.1/emulator`.
- Created AVD `Holodeck_Pico_Swan` under `C:/Users/daren/.pico/avd`.
- `pico-cli emulator doctor --format json` reports SUCCESS, recognizes the installed bundle and AVD, and reports virtualization prerequisites ready.
- Boot is NOT verified. CLI launch exits with `can't find the emulator executable` after selecting a nested SDK root. An explicit-path launch also exited without reaching adb readiness. No Pico runtime or app acceptance pass is claimed.
- Initial CLI launch log: `C:/Users/daren/.pico/cli/logs/pico-emulator-2026-10-27T07-26-21-669Z.log`. Explicit-path logs: `holodeck-explicit-boot.stdout.log` and `.stderr.log` in the same directory.
- Approximately 32 GiB remained on C: immediately after extraction (owner is continuing to offload files).
- Unity package integration remains pending: this chat exposes neither `pico_xr_package` nor `pico_xr_status`. Per installed PICO package-manager guidance, no manifest/package mutation was attempted without those tools.

## Startup and editor-bridge repair verified 2026-10-27

- Fixed emulator startup by including the installed bundle root, `lib64`, and `lib64/qt/lib` in the process DLL search path and calling the bundled QEMU executable directly with explicit SDK/system-image paths. The CLI launcher had selected an incorrect nested SDK root; direct QEMU initially exited with Windows status 0xC0000135 until the bundled Qt DLL directory was included.
- Added `tools/Start-PicoEmulator.ps1` as the repeatable Windows workaround. It launches in the background, defaults to dedicated port 5590, refuses an already occupied emulator port, and disables snapshot saving. PowerShell parser validation passed.
- Actual boot verified through bundled adb: serial `emulator-5590`, AVD `Holodeck_Pico_Swan`, `sys.boot_completed=1`, Android 16, API 36, ABI x86_64. This is boot/connectivity evidence only, not Holodeck feature parity.
- Replaced the stale `.codex/config.toml` relay entry (the referenced executable did not exist) with the installed Unity CLI `unity.exe mcp --project-path <repo>`. Preserved the other MCP entry and network policy. TOML parsing and executable existence checks passed.
- Installed official `com.unity.pipeline` 0.9.1-exp.1 through Unity CLI. Started Unity 6000.2.10f1 in Android batch mode; live CLI status reports `ready` on port 7800 and enumerates 166 editor tools. No app APK was built by this repair.
- Project-local MCP configuration is for HeadsetHolodeckDev. A client must load/reload that project's configuration to expose the connection in its own tool registry; CalendarAlarm's running chat has not dynamically acquired these tools.
- Pico-specific extension/SDK initialization remains pending explicit `/pico-unity-init` invocation, as required by the installed initialization skill. The live base editor currently has no `pico_xr_package` or `pico_xr_status` tools.
- Free C: space after verification: approximately 28.75 GiB. All installed files and device data remain on C:.


## Pico initialization completed 2026-10-27

- Owner authorized initialization and any required editor replacement; compatibility checks allowed retaining Unity 6000.2.10f1 with installed Android SDK/NDK/OpenJDK modules. No editor was removed or replaced.
- Existing-project bootstrap completed with mode `openxr`, device `pico swan`, platform `android`, business type `toC` (consumer application). Bundled `write_config.py` wrote `.pico-cli/config.json` with `pico_unity_init_completed=true`. No template replaced existing assets.
- Installed AI Assistant 2.17.0-pre.1, PICO XR SDK 6.1.1, SpatialAdapter 6.1.0, MCP Extensions 0.1.1, Pico glTF exporter and required Input System 1.18.0. Git revisions are captured by Packages/packages-lock.json. Existing modified World Labs and Sherpa packages were preserved.
- Fixed the SpatialAdapter USD export compile failure using the Pico USD fork specified in the official OpenXR template, rather than modifying cached SDK sources: `https://github.com/Pico-Developer/usd-unity-sdk.git?path=package/com.unity.formats.usd#dev`.
- The project's MCP base dependency at initialization was 1.5.0; upgraded the incompatible particle extension 1.2.23 to 2.0.2, which explicitly depends on MCP 1.5.0. This resolved its missing ComponentRef.Matches errors.
- Unity compiled successfully, completed follow-up setup-symbol reloads, and reports ready. Pico auto-setup enabled `ENABLE_PICO_OPENXR_SDK`; global project/XR settings changed during import. Reproducible Meta/Pico target selection is still required before claiming app parity.
- Installed relay `~/.unity/relay/relay_win.exe` 1.0.12-build.97. Actual MCP initialize/tools-list succeeded: 17 total tools, including all 10 Pico tools. A read-only `pico_xr_status` call succeeded and reported runtime `openxr`. Evidence: ignored Logs/pico-mcp-tools.json and Logs/pico-mcp-status.json. No XR Origin was created or modified by this status probe.
- Added project-local `pico-unity` MCP entry using the verified relay and explicit project path; retained the separate CLI Pipeline entry. A client must load this project's configuration to acquire the new tool registry.
- Original manifest, lockfile and editor version backup: Logs/pico-init-backup-20261027-124235/.
- Remaining warnings: SDK spatial-audio plugin metadata references absent binaries; glTF exporter includes an older Newtonsoft assembly, which Unity ignores in favor of the installed newer assembly. These did not prevent editor compilation; spatial audio/player packaging are not verified.
- No new Meta/Pico APK or runtime feature acceptance was performed by this initialization. Run the planned Meta regression and isolated Pico app build next. C: has approximately 26.5 GiB free; D: remains unused.
