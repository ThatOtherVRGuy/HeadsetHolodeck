# Meta Quest baseline

Existing evidence: [stabilization status](../../stabilization/STATUS.md) and
[handoff 06](../../stabilization/handoff-06.md). Preserve those records.

After the folder migration, perform a fresh Unity import and Quest smoke test:

1. Compile and run the existing editor regression entry points.
2. Build the retained Holodeck scene with device credentials.
3. Launch, wake, load cached world, manipulate, add object/audio, save, restart and restore.
4. Check hands/controllers, LCARS UI, camera capture and provider error handling.
5. Repeat after switching the project to Pico and back once target switching exists.

Record new results here with date, commit, configuration, device/OS, logs, and limitations.
The [Unity validation report](FOLDER_VALIDATION.md) records successful import, compilation,
scene loading (1,789 objects, zero missing scripts), and an Android APK build after license
activation and local dependency restoration. Unity exited 0. A headset runtime smoke test
remains pending.
