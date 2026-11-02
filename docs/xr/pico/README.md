# Pico Swan work queue

Start with [the emulator parity plan](EMULATOR_PARITY_PLAN.md), checked 2026-10-27. The emulator
and live SDK/MCP tools work. The unchanged Meta APK installs/launches but fails OpenXR startup;
target/manifest isolation and a proper Pico build are the immediate tasks.

Follow [the shared XR plan](../PLAN.md) in order. XR-01 import, scene validation and Android build
passed; editor regression/device checks remain. XR-02 emulator and target configuration are next.
Initial toolchain inventory and historical storage findings are in
[PICO_SWAN.md](../../PICO_SWAN.md).

The [development environment report](DEVELOPMENT_ENVIRONMENT.md) records installed Unity/Pico AI
tooling, successful emulator boot, completed OpenXR initialization, successful compilation and a verified live Pico MCP connection.

Store future emulator and hardware parity reports separately here. Every result must identify
the commit, SDK/emulator version, device/runtime, steps and evidence. Emulator success does not
establish physical room alignment, eye tracking quality or sustained headset performance.

Pico SDK/tooling initialization and emulator boot are verified. App target isolation, a post-integration Meta regression build, and Pico feature acceptance remain pending.
