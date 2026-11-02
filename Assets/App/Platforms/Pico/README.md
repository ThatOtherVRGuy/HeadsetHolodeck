# PICO Swan / PICO OS 6

`Settings/Pico.asset` is the existing Android build profile, relocated with its original `.meta` GUID.
It still points at the existing Holodeck scene. It is a starting configuration, not a verified Swan build.

Future Pico-only camera, passthrough, environment, permission, and eye-tracking adapters belong here.
Use the common application code in `../../Shared`; do not fork world generation, command routing,
UI, save formats, or object caches into this directory.

Toolchain inventory and installation blocker: [PICO_SWAN.md](../../../../docs/PICO_SWAN.md).
Implementation sequence and acceptance gates: [PLAN.md](../../../../docs/xr/PLAN.md).
