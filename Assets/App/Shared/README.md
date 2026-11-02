# Shared application code

This is the single implementation used by Meta Quest and PICO Swan.

- `Scripts/`: state, input, voice, direct coordination, world/object providers, and diagnostics.
- `Command/`: speech intent, conversational targeting, hand interaction, world manipulation, and UI.
- `Save/`: persistence runtime and its existing test assembly.
- `Editor/`: scene setup, build tooling, validators, and editor tests.

Existing namespaces, assembly names, and Unity asset GUIDs are preserved. Common prefabs, materials,
input assets, audio, and UI remain in their existing sibling folders under `Assets/App`.

New hardware-dependent implementations belong under `../Platforms/Meta` or `../Platforms/Pico`.
Shared code should consume capability interfaces and must not directly depend on a vendor SDK.
The existing camera service still contains Horizon-specific permission handling; extracting it is
planned, not completed. The existing scene is also still configured for the Meta baseline.

See [the architecture and delivery plan](../../../docs/xr/PLAN.md).
