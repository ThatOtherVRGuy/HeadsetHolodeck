# Meta Quest baseline

Retain the existing Quest behavior as the reference for feature parity.

- Scene: `Assets/Scenes/Holodeck.unity` (existing GUID and scene path retained).
- Shared runtime and editor tooling: `Assets/App/Shared`.
- OpenXR assets: `Assets/XR`; renderer/settings assets: `Assets/Settings`.
- Android manifest and native plugins: `Assets/Plugins/Android` and `Assets/Plugins/SherpaOnnx`.
- Verification history: `docs/stabilization/STATUS.md` and `handoff-06.md`.

These assets remain at their established locations until target configuration can reproduce their
behavior. Do not copy the Pico profile to create a Meta profile: its Player Settings include Pico
overrides. Capture the current Quest settings in a dedicated Meta profile in the configuration phase.

Future Meta-only camera, passthrough, environment, permission, and tracking adapters belong here.
Keep the original script GUID if an existing component is moved; changing its namespace or assembly
requires a separate serialized-component migration.
