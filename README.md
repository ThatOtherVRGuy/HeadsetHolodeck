# Headset Holodeck

Headset Holodeck is a Unity XR project for creating and revisiting immersive worlds in a headset using voice, hands, gaze, image prompts, cached worlds, and spatial audio. The current public build focuses on World Labs world creation, OpenAI-backed command interpretation, Sherpa-ONNX local voice activation/TTS, local or remote splat loading, camera/image prompts, and interactable primitive object creation.

## Quick Start

1. Install Unity `6000.2.10f1` through Unity Hub.
2. Clone this repository:

   ```bash
   git clone https://github.com/ThatOtherVRGuy/HeadsetHolodeck.git
   ```

3. Open the cloned `HeadsetHolodeck` folder in Unity Hub.
4. Let Unity import the project. The first import can take several minutes.
5. Copy `.env.example` to `.env` in the project root and add your own keys.
6. Open `Assets/Scenes/Holodeck.unity`.
7. Run `Headset Holodeck > Validate Install` from the Unity menu.
8. Press Play in the Editor, or build to Quest from `File > Build Profiles`.

## API Keys

Create a project-root `.env` file next to `Assets/`, `Packages/`, and `ProjectSettings/`.

```env
OPENAI_API_KEY=your_openai_key
WORLDLABS_API_KEY=your_worldlabs_key
PIXABAY_API_KEY=your_pixabay_key
FREESOUND_API_KEY=your_freesound_key
XENO_CANTO_API_KEY=your_xeno_canto_key
```

Required for the main demo path:

- `OPENAI_API_KEY`: command interpretation and API-backed speech intent.
- `WORLDLABS_API_KEY`: World Labs world generation.

Optional but useful:

- `PIXABAY_API_KEY`: image search prompts.
- `FREESOUND_API_KEY`: Freesound audio search.
- `XENO_CANTO_API_KEY`: bird/wildlife audio search.
- `MESHY_API_KEY`, `TRIPO_API_KEY`, `HITEM_API_KEY`: reserved for object-generation experiments.

Never commit `.env`. The repo ignores it.

## What Is Included

This repository intentionally vendors two modified packages so testers get the exact code used by the app:

- `Packages/com.worldlabs.gaussian-splatting`
- `Packages/com.ponyudev.sherpa-onnx`

The repo also includes the currently configured Sherpa-ONNX runtime files and smaller model assets:

- int8 Zipformer ASR model files under `Assets/StreamingAssets/SherpaOnnx/asr-models`
- Silero VAD model under `Assets/StreamingAssets/SherpaOnnx/vad-models`
- Kristin Piper/VITS TTS model under `Assets/StreamingAssets/SherpaOnnx/tts-models`
- Android and macOS Sherpa native libraries under `Assets/Plugins/SherpaOnnx`

This means a tester should not need to install separate package forks or pull pending package PRs.

## Large Files And Optional Downloads

The required smaller/int8 model assets are already included. The unused full-precision ASR ONNX files and test WAVs are not included to keep the public repo lighter.

If you want the full ASR model package for experimentation, download it from the Sherpa-ONNX model release:

- [sherpa-onnx-zipformer-small-en-2023-06-26.tar.bz2](https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-zipformer-small-en-2023-06-26.tar.bz2)

For the current project configuration, those full-precision files are optional.

## Quest Build Notes

Target device is Meta Quest 3.

Recommended settings are already in the project, but verify:

- Platform: Android
- Graphics API: Vulkan
- XR: OpenXR enabled
- OpenXR render mode: Multi-pass
- URP active
- Microphone permission present
- Camera/headset-camera permissions present if testing real-world image capture

The Android manifest is included at `Assets/Plugins/Android/AndroidManifest.xml`.

## Validation Helper

Use `Headset Holodeck > Validate Install` inside Unity. It checks:

- Unity version
- main scene
- vendored package folders
- Sherpa settings, models, and native libraries
- Android manifest permissions
- `.env` presence and which keys are configured
- common generated folders that should not be committed

The validator does not print secret values.

## Common First-Test Flow

1. Open `Assets/Scenes/Holodeck.unity`.
2. Confirm `Headset Holodeck > Validate Install` has no required failures.
3. Press Play.
4. Say a wake phrase such as `Computer`.
5. Try:

   ```text
   Computer, create a castle on a snowy mountain at sunset.
   ```

6. Try image prompt flows from the LCARS UI:
   - camera capture
   - image search
   - create world from prompt plus image

7. Try audio:

   ```text
   Add sounds of the ocean.
   Make it quieter.
   Stop all sounds.
   ```

## Troubleshooting

- If voice does not respond, run the validator and check microphone permission, Sherpa model files, and `OPENAI_API_KEY`.
- If World Labs creation fails, check `WORLDLABS_API_KEY` and internet access.
- If image search buttons are disabled, add `PIXABAY_API_KEY`.
- If audio library results are weak or unavailable, add `FREESOUND_API_KEY` and/or `XENO_CANTO_API_KEY`.
- If Quest builds fail around Android permissions, confirm `Assets/Plugins/Android/HeadsetCameraPermissions.androidlib/build.gradle` has a namespace and compile SDK.
- If splats do not render on Quest, do not enable URP compatibility mode unless the Gaussian splatting package specifically supports it.

## Developer Notes

Most scripts under `Assets/App/Editor` are setup and repair utilities used while building the scene. A tester should normally only need:

- `Headset Holodeck > Validate Install`
- optional scene setup utilities if a scene is accidentally damaged

Long term, the vendored packages can move to public package forks pinned by commit. For now, vendoring keeps the tester install path simple and reproducible.
