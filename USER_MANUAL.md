# HeadsetHolodeck User Manual

HeadsetHolodeck is a Unity XR prototype for creating and revisiting immersive worlds from voice commands. In the main flow, you speak a prompt, the app transcribes it, sends it to World Labs, and loads the generated world into the headset. The app can also load local or remote splat/panorama files, switch between world views, and save or restore world configurations.

This manual describes the app as it currently stands in the `Holodeck` scene.

## Requirements

- Unity `6000.2.10f1`.
- The project opened from `/Users/davidarendash/Documents/Projects/Unity/HeadsetHolodeck`.
- An XR headset build target, typically Android/OpenXR.
- Internet access for OpenAI transcription/intent calls and World Labs generation.
- Microphone access enabled for the app.
- World Labs API access configured in the project-root `.env` file.
- OpenAI access configured for the current local prototype.
- Optional sound search access: `FREESOUND_API_KEY` for Freesound and `XENO_CANTO_API_KEY` for xeno-canto bird recordings. Openverse can be used without a local key.

## Before You Run

1. Open the project in Unity Hub using Unity `6000.2.10f1`.
2. Open `Assets/Scenes/Holodeck.unity`.
3. Confirm the active build target is Android if you are building for a headset.
4. Confirm the World Labs `.env` file exists at the project root.
5. Confirm microphone permission is allowed on the device.
6. Press Play in the Editor, or build and run to the headset.

The main scene included in the build settings is `Assets/Scenes/Holodeck.unity`.

## Basic Voice Flow

The app uses push-to-talk style recording.

1. Press the wake/record button once.
2. Speak your command.
3. Press the wake/record button again to stop recording.
4. Wait while the app transcribes your speech and performs the action.

In the Editor, the documented keyboard shortcut is `Space`. On headset, the wake action is wired through the project input actions, currently intended for the right controller secondary button.

## Generate A New World

Say a world-generation request naturally:

- "Create a moonlit alien forest with glowing mushrooms."
- "Generate a peaceful mountain temple at sunrise."
- "Make a cyberpunk street market in the rain."

After transcription, the app sends the prompt to World Labs. While the full splat loads, the app may show a panorama preview if one is available. When loading finishes, the app switches to the ready state and fades the preview.

Generation can take several minutes. The current timeout is long enough for normal World Labs jobs, but the app may appear idle while waiting on remote generation.

## Choose Generation Quality

You can change the active World Labs model tier by voice:

- "Use draft quality."
- "Use fast generation."
- "Use standard generation."
- "Use high quality."

Available tiers are `draft`, `fast`, `standard`, and `high`. Higher quality may take longer.

## Switch Views

The app supports multiple ways to view a world, depending on what content is available.

Try commands like:

- "Show the 3D world."
- "Switch to panorama view."
- "Show the mesh view."

If the requested view is not available for the current world, the app should report that it cannot switch to that view.

## Load Local Or Remote Content

The app can load splat and panorama content from local files or URLs.

Supported splat formats:

- `.spz`
- `.ply`

Supported panorama formats:

- `.jpg`
- `.jpeg`
- `.png`

You can use the content loading UI or voice commands such as:

- "Load the splat from [path or URL]."
- "Load the panorama from [path or URL]."

Local content defaults to the app's persistent `WorldContent` folder unless the UI is configured to use the saved-world cache. The Local Files panel scans for supported files and creates a row for each one. The URLs panel lets you enter a URL and keeps a small history of recent entries.

## My Worlds And Saved Configurations

The app has a saved-world configuration system. It can save world metadata, cached splats/panoramas, and object changes.

Useful commands:

- "Show my worlds."
- "Save this world."
- "Save as Desert Temple."
- "Load Desert Temple."

The My Worlds panel shows saved configurations as cards. Each card can load a saved world, save a copy under a new name, or delete the config.

Deletion is immediate in the current prototype, so treat delete buttons carefully.

## Object And Scene Commands

The speech-intent layer can route commands for scene manipulation when the relevant scene controllers are wired.

Examples:

- "Move this object over there."
- "Make it bigger."
- "Rotate it 45 degrees."
- "Reset its transform."
- "Put a cube where I'm pointing."
- "Set the lighting to sunset."
- "Point the sun this way."

Targeted commands depend on what the app can infer from pointing, recent interactions, object names, and the current scene context. If the app cannot resolve the target, the command may do nothing or log a warning.

## World Sounds And Ambience

When a generated World Labs world finishes loading, the app can create matching ambience automatically. The current implementation infers likely sound layers from the world name or prompt, such as forest birds, wind in trees, river water, rapids, ocean waves, rain, thunder, cave drips, or distant city ambience.

You can also ask for sounds directly without changing the visual world:

- "Add birds in the trees."
- "Play river rapids and wind."
- "Add ocean waves around me."
- "Add a red-tailed hawk call."

For collective sound requests, the app may create multiple spatial `AudioSource` objects, one per layer. Audio files are downloaded into the saved-world cache when the save system is wired, and explicit sound additions are registered as scene object changes so they can be saved with the current world configuration.

The app chooses a playback style automatically unless you ask for one. Continuous backgrounds such as ocean, rain, rivers, wind, crowds, traffic, or machinery loop. Discrete event sounds such as doors, impacts, buttons, or footsteps play once. Natural calls and sparse events such as birds, frogs, insects, thunder, bells, or chimes play at intervals, usually with randomized timing.

The reusable prefab for interval-based sounds is `Assets/App/Prefabs/RandomIntervalAudioSource.prefab`. It contains an `AudioSource` and `AudioPlaybackController`, defaulting to random interval playback every 10 seconds with 3 seconds of variance.

You can control existing sounds by voice:

- "Make the river louder."
- "Make the birds quieter."
- "Mute the rain."
- "Unmute it."
- "Play the hawk now."
- "Play the birds every 20 seconds."
- "Play the thunder at random intervals with a 10 second variance."
- "Make the crowd ambient."
- "Make the river spatialized."

Ambient means 2D audio with `spatialBlend` set to `0`. Spatialized means 3D audio with `spatialBlend` set to `1`.

Sound providers are tried automatically. Freesound is the preferred general sound-effects source when `FREESOUND_API_KEY` is available, Openverse is the no-key fallback, and xeno-canto is used for bird or birdsong requests when configured.

Downloaded audio sources store attribution metadata with the saved world, including provider, title, creator/recordist, license, license URL, landing URL, source URLs, prompt, tags, duration, cached file name, and downloaded byte count. This metadata is intended for the world information and attribution UI.

## UI Commands

Some panels can be shown by voice if they are registered in the scene's UI panel controller.

Examples:

- "Show my worlds."
- "Open the content loader."

Panel names are matched by configured keys, so exact behavior depends on the current scene setup.

## Audio Feedback And TTS

The app has hooks for assistant responses and audio feedback. When the speech-intent model returns a spoken response, it is routed through the app events. Depending on scene wiring, this may appear as UI text, text-to-speech, or console output.

## Troubleshooting

### Nothing happens when I speak

- Make sure you pressed the wake button once to begin recording and again to stop.
- Check that microphone permission is granted.
- In the Editor, try the `Space` key.
- Check the Unity Console for transcription or recorder errors.

### The app says no audio was recorded

- Confirm the headset or computer microphone is selected and working.
- Speak after recording has started.
- Avoid very short recordings.
- On Android, permission may still be pending when recording starts.

### World generation does not start

- Confirm the OpenAI transcription step succeeded.
- Confirm internet access.
- Confirm the World Labs key is available in the project-root `.env`.
- Check the Console for World Labs API errors.

### The panorama appears but no 3D world loads

- The generated world may not have a usable splat URL.
- The splat download may have failed.
- The runtime splat loader may have failed while processing the file.
- Try switching to panorama view if the panorama is available.

### Local file loading fails

- Confirm the path or URL ends in a supported extension.
- For local files, confirm the file exists in the expected folder.
- Use `.spz` when possible. `.ply` support exists, but is more experimental in the current runtime path.

### Saved worlds do not appear

- Open the My Worlds panel and refresh by closing/reopening it.
- Confirm that the world config store has scanned the persistent Worlds folder.
- Check whether the app has write access to its persistent data path.

### Requested sounds do not play

- Confirm internet access.
- For Freesound, confirm `FREESOUND_API_KEY` is available in the local environment or assigned on the provider component.
- For xeno-canto bird searches, confirm `XENO_CANTO_API_KEY` is available if the provider requires one.
- Check the Unity Console for search, download, or audio decode warnings.

## Current Prototype Limitations

- This is a local prototype, not a hardened distribution build.
- API credentials are currently handled locally.
- Remote generation and loading are only partially cancellable.
- Some voice commands depend on scene references being assigned correctly.
- Some UI panels and assistant responses depend on current scene wiring.
- Sound search depends on third-party library availability, licensing metadata, and whether the returned file can be decoded by Unity on the current platform.
- `.ply` loading is more experimental than `.spz` loading.
- Delete actions in My Worlds are immediate.

## Quick Command Examples

- "Create a quiet redwood forest at dawn."
- "Use high quality."
- "Show the panorama."
- "Show the 3D world."
- "Show my worlds."
- "Save as Redwood Dawn."
- "Load Redwood Dawn."
- "Load the splat from example.spz."
- "Load the panorama from example.jpg."
- "Make this bigger."
- "Rotate this 45 degrees."
- "Reset it."
- "Add forest birds and wind in the trees."
- "Play river rapids over there."
