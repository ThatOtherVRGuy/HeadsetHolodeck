# Headset implementations

- `Meta/`: retained Quest baseline and future Meta-specific adapters/settings.
- `Pico/`: PICO Swan / PICO OS 6 settings and future Pico-specific adapters.

Keep gameplay, provider clients, voice routing, UI, save formats, and targeting policy in
`../Shared`. Put hardware access and target composition here. A folder name does not isolate code
from compilation: vendor adapters will need explicit assembly definitions, references, and
target defines when SDKs are introduced.

The shared Android OpenXR configuration is currently global. Selecting a build profile alone does
not yet switch it safely between vendors. Implement and verify target configuration before using
the Pico profile for acceptance builds.

See [the architecture and delivery plan](../../../docs/xr/PLAN.md).
