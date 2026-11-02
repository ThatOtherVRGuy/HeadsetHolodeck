# Headset Holodeck User Manual

Headset Holodeck is a Unity XR prototype for creating, loading, revisiting, and modifying immersive worlds in a headset. You can create worlds from voice prompts, captured camera images, or online image search results; load local or remote splats and panoramas; save worlds for fast revisiting; add spatial ambience; and use hands, gaze, and posture as part of object placement and editing.

This manual describes the current `Assets/Scenes/Holodeck.unity` scene.

## Requirements

- Unity `6000.2.10f1`.
- Meta Quest 3 or Unity Editor for local testing.
- Microphone permission.
- Internet access for OpenAI, World Labs, image search, and online audio libraries.
- A provider `.env` for API-backed features: the project root when you run in the Editor, and the
  headset's own `files/.env` when you run an installed device build (see the README's
  "Headset Run Flow").
- Optional Quest camera/headset-camera permission for real-world image capture.

Useful keys:

- `OPENAI_API_KEY`: speech intent and OpenAI-backed command interpretation.
- `WORLDLABS_API_KEY`: World Labs world creation.
- `PIXABAY_API_KEY`: online image search.
- `FREESOUND_API_KEY`: Freesound audio search.
- `XENO_CANTO_API_KEY`: bird/wildlife audio search.
- `THREEDAISTUDIO_API_KEY`: 3dAIStudio Tripo image-to-3D/text-to-3D object generation (the primary
  object provider; it has a credit preflight and a credit estimate before it generates).
- `HITEM_ACCESS_KEY` + `HITEM_SECRET_KEY`: HiTem image-to-3D object generation.
- `MESHY_API_KEY`, `TRIPO_API_KEY`: reserved. They only satisfy the "an object provider is
  configured" check; no Meshy or standalone Tripo provider exists in this build.

Openverse audio search needs no key. Setting the object-generation keys is optional — without them
the object-generation UI is disabled and the app tells you which key to add.

Run `Headset Holodeck > Validate Install` in Unity after cloning. It checks the scene, packages, Sherpa models, Android permissions, and local key configuration without printing secret values.

## Starting The App

1. Open the project in Unity `6000.2.10f1`.
2. Open `Assets/Scenes/Holodeck.unity`.
3. Confirm `.env` exists at the project root if using API-backed features.
4. Press Play in the Editor, or build and run to Quest.

The main environment starts in the static holodeck arch world. The Arch UI is visible by default and can be hidden or shown by voice.

## Voice Input

The app now supports hands-free wake-word input and push-to-talk fallback.

### How A Command Becomes An Action

Each phrase is interpreted as one **intent** — a structured command such as "create an object",
"move this", or "load a saved world" — rather than as free text. The app fills in the details it
heard (the object, the target, the distance, the direction) and acts only when the action can be
carried out without guessing. If a detail is missing it asks one short follow-up ("How many degrees
should I rotate it?"), and if the phrase is not a command it says so instead of guessing.

The same idea can arrive in several phrasings:

- "Computer, create a ball in my left hand." — place an object, anchored to the left hand.
- "Computer, move the red cube up one meter." — move a named target one meter up.
- "Computer, make the river quieter." — change an existing sound, not add a new one.
- "Computer, load Redwood Dawn." — restore that saved world; this is never treated as a request to
  generate a new world.

The full field reference, every intent type, and worked examples are in the
[developer guide](docs/intent.md).

### Wake Word

Say `Computer` followed by a command:

- "Computer, create a cafe in Paris in the 1920s on a rainy day."
- "Computer, add sounds of the ocean."
- "Computer, make the red cube larger."

Inline commands are supported. If you only say "Computer", the app enters a short listening window and waits for the next utterance.

The wake system uses local VAD plus Sherpa ASR today. The configured wake list is a deliberate alias
set: `computer, pewter, pudor, peter, impewter, pumpor`. The VAD/ASR front end often begins mid-word
and hears only the last syllables of "computer", so those accepts are by design, not a defect. If the
app ignores a different mis-hearing (`fudor`, `letter`, `future`, …), that is a real limitation: the
alias list can be widened or the speech front end replaced, but both are deliberately deferred for
now. During text-to-speech playback, wake detection is suppressed so the app does not wake itself.

### Push-To-Talk Fallback

Push-to-talk is still available for testing and fallback. In the Editor, the shortcut is `Space`. On device, the wake/record action is wired through the project input actions for controller input.

Push once to start recording, speak, then push again to stop and process.

## Exiting The Holodeck

You can exit in two ways:

- Say "Computer, exit holodeck."
- Walk through the Exit door/trigger in the static holodeck environment.

Both paths call `Application.Quit`. On Android, the exit script also asks the activity to finish so the app closes more reliably on headset.

If you want to end the current generated world without closing the app, say:

- "Computer, end program."

This returns to the static holodeck, clears world audio, resets the world info panel, and moves `Me` back to the configured Teleport Anchor when that scene reference is wired.

## Creating Worlds

Say a natural world prompt:

- "Computer, create a moonlit alien forest with glowing mushrooms."
- "Computer, generate a peaceful mountain temple at sunrise."
- "Computer, make a cyberpunk street market in the rain."

The app sends the prompt to World Labs, shows World Labs status on the LCARS panels, loads available panorama/splat content, and saves metadata for revisiting.

When a new world loads or "end program" runs, world audio and loaded world assets are cleared so the previous world does not linger.

## Generation Mode

The current World Labs creation mode is visible on the LCARS operations UI at all times. You can change it by touch or by voice:

- "Computer, use draft quality."
- "Computer, use fast generation."
- "Computer, use standard generation."
- "Computer, use high quality."

The available modes are `Draft`, `Fast`, `Standard`, and `High`. The active mode is highlighted in blue; inactive buttons use the LCARS orange palette. When a saved/generated world includes creation-mode metadata, the UI tries to reflect that mode when the world is loaded.

## Image And Camera Prompting

World prompts can use images as inspiration. The image can come from the headset camera or from Pixabay image search.

### Headset Camera Capture

Use the Camera panel or say:

- "Computer, capture image."
- "Computer, open camera."
- "Computer, take photo."

The app opens a live camera preview. When you like the frame, say:

- "Computer, OK."
- "Computer, shoot."
- "Computer, capture."

The captured image appears on the LCARS preview panel and becomes the current image prompt. You can then say:

- "Computer, create world from image, make it hyper realistic."
- "Computer, make world from capture, turn it into an ancient temple."
- "Computer, create object from image."

Object creation from image is implemented for the configured 3dAIStudio Tripo or HiTem provider. Its
buttons are disabled until `THREEDAISTUDIO_API_KEY` (or `HITEM_ACCESS_KEY` + `HITEM_SECRET_KEY`) is
configured. Generation shows a spinner while the provider works, costs provider credits, and the
finished model is cached under the world's save folder, so a later restore re-imports it locally
instead of paying for it again.

### Online Image Search

Use the Image Search panel or say:

- "Computer, search images redwood forest."
- "Computer, search Pixabay neon Tokyo street."
- "Computer, next image."
- "Computer, previous image."
- "Computer, use this image."

After selecting an image, create a world from it with the UI button or voice command. Pixabay attribution is shown in the panel where available. Image-search buttons are disabled if `PIXABAY_API_KEY` is missing.

## Capturing Thumbnails And Panoramas

Sideloaded splats may not come with thumbnails or panoramas. The app can capture them from the currently loaded world and store them in the saved world's folder.

Voice commands:

- "Computer, capture thumbnail."
- "Computer, capture panorama."
- "Computer, save thumbnail."
- "Computer, save panorama."

The capture runs on a 3-second countdown. Thumbnail capture uses the current headset/camera pose. Panorama capture renders an equirectangular image. If a panorama exists and no explicit thumbnail exists yet, the panorama can be used as the world card image until replaced by a thumbnail.

The Arch and configured UI objects are temporarily hidden during world-view capture so they do not appear in the thumbnail or panorama.

## Loading Local Or Remote Content

The Local/URL panel can load splats and panoramas from local files or internet URLs.

Supported splat formats:

- `.spz`
- `.ply`

Supported panorama/image formats:

- `.jpg`
- `.jpeg`
- `.png`

You can use the panel, virtual keyboard, or voice commands such as:

- "Computer, load the splat from [path or URL]."
- "Computer, load the panorama from [path or URL]."

The app also keeps URL history and local file lists. The virtual keyboard appears for text fields so the app can be used without a physical keyboard or voice availability.

## My Worlds And Cached Worlds

The My Worlds panel lists local saved worlds. It supports loading, saving as a new local config, and deleting saved configs.

Useful commands:

- "Computer, show my worlds."
- "Computer, save this world."
- "Computer, save as Desert Temple."
- "Computer, load Desert Temple."

Worlds are cached for faster revisiting. The world info panel shows name, dates, source, object/prompt counts, file sizes where calculable, model mode where known, and attribution. When no generated world is loaded, the static world attribution is:

```text
model by Set Blueprint Archive
```

### How Saves Are Written

Every save is journaled rather than overwritten in place. The app writes a temporary file, rotates the
previous committed document to `world.json.bak`, then renames the new one into place, and it holds a
per-folder writer lock while doing so. If the app is killed partway through a save, or the primary
file is damaged, the next startup recovers the previous generation from `world.json.bak` and keeps
both files; the next successful save repairs the primary.

What that means in practice: a save interrupted mid-write costs you the newest changes, never the
world. On a headset, saves live at
`/sdcard/Android/data/com.QLC.HeadsetHolodeck/files/Worlds/<config_id>/world.json`. Restarting the
app and loading the same world restores its saved transform, spawn point, objects, downloaded audio,
and cached 3D content.

## Status, Info, And LCARS UI

The Arch has two main UI pillars and a crossbeam status area.

- Operations side: My Worlds, World Labs, Files/URL, Camera, Image Search, creation controls, model mode buttons.
- Status/info side: current world metadata, dates, sizes, source, status, and attribution.
- Crossbeam: realtime clock, total app runtime, current-world runtime, status ticker, and warning/error flashing.

If no world is loaded, the current-world timer displays `--:--:--`.

Voice examples:

- "Computer, show arch."
- "Computer, hide arch."
- "Computer, show my worlds."
- "Computer, open the content loader."

## World Sounds And Ambience

When a World Labs world loads, the app can infer appropriate ambience from the prompt or world metadata. For example, a Roman coliseum prompt may produce crowd, cheering, armor, or arena ambience rather than a generic electronic loop.

You can add sounds without changing the visual world:

- "Computer, add sounds of the beach."
- "Computer, add seagull sounds."
- "Computer, add forest birds and wind."
- "Computer, add river rapids."

For collective requests, the app may create multiple audio sources. Background ambience such as rain, ocean, crowds, rivers, wind, traffic, or machinery loops. Discrete events play once. Natural calls and sparse events such as birds, frogs, insects, thunder, bells, and chimes play at randomized intervals.

Audio controls:

- "Computer, stop all sounds."
- "Computer, mute all sounds."
- "Computer, unmute all sounds."
- "Computer, make the river louder."
- "Computer, make the birds quieter."
- "Computer, play seagull sounds."
- "Computer, make the crowd ambient."
- "Computer, make the river spatialized."

The app first looks for existing matching audio by canonical name or object name before downloading a new clip for "play" commands. World audio is destroyed when the world ends or another world loads; UX audio such as TTS and cues is kept separate.

Downloaded audio stores attribution metadata, including provider, title, creator/recordist, license, source URL, prompt, tags, duration, cached file name, and byte count.

## Object Creation And Editing

The app can create built-in interactable primitives and wrap them with XR interaction components.

Examples:

- "Computer, create a cube where I'm pointing."
- "Computer, create a sphere at world origin."
- "Computer, create a ball in my left hand."
- "Computer, put a cube in my right hand."
- "Computer, create a capsule in front of me."

Objects get default URP-compatible materials. If no material is specified, a medium gray material is used. Materials are reused through a runtime material catalog.

Material commands:

- "Computer, make this red."
- "Computer, make the cube blue metallic."
- "Computer, make all cubes matte black."

Material adjectives can also identify targets:

- "Computer, move the red cube up one meter."
- "Computer, make the red metallic sphere smaller."

If more than one target matches, the app should ask for clarification, such as "Which red cube?"

## Movement, Targeting, And Spatial Language

The app uses recent interactions, object names, gaze, hand/controller pointing, and body-relative language to resolve targets.

Examples:

- "Computer, move me one meter forward."
- "Computer, move me one meter to my left."
- "Computer, move the cube in front of me."
- "Computer, move the sphere to my right hand."
- "Computer, delete this cube."
- "Computer, make this larger."

`This` and `that` use pointing/gaze context where available. `All` commands affect matching app-created scene objects, not UX systems.

The app also tracks "my parts":

- head: `Main Camera`
- left hand: active left hand/controller object
- right hand: active right hand/controller object

## Hands And Direct Manipulation

Hands are a first-class input alongside voice and controllers (XR Hands + XR Interaction Toolkit).
Pinch or grip near an app-created object to point at it, grab it, or set the "held" object that `this`
and `that` resolve to. Hand pointing feeds the same target resolver as gaze and controllers, so
"what is this", "move this", and "make this red" work from whichever pointer is aiming at the object.

The whole loaded world can also be manipulated directly:

- "Computer, grab world." (or the Ops panel's `GRAB WORLD` button) arms world manipulation.
- Move and rotate the world by moving/rotating the dominant hand; grip with the other hand to switch
  to two-handed midpoint translation, vector rotation, and uniform two-hand scaling.
- "Computer, release world." commits the final world transform to the save.
- Snap settings persist between runs: "snap to 10 centimeters", "snap to 30 degrees", "snap on",
  "snap off". Snapping applies to grabbed objects and to objects moved by voice.

## Delete Commands

You can delete objects or categories:

- "Computer, delete this cube."
- "Computer, delete all cubes."
- "Computer, delete all sounds."
- "Computer, delete the red sphere."

For "this" commands, point or look at the object when speaking. Delete commands are intended for app-created/interactable scene content and world audio, not the LCARS UI or system objects.

## Lighting And Sun Direction

Lighting commands depend on spatial context and scene wiring:

- "Computer, set lighting to sunset."
- "Computer, point the sun this way."

For directional commands, point with a hand/controller so the app can infer the intended direction.

## Virtual Keyboard

When voice is unavailable, or when entering URLs/search text, use the LCARS virtual keyboard. It appears when supported text fields are selected, fades in, updates a preview field and the target field, then fades out and disables itself when dismissed to avoid rendering cost in VR.

## Troubleshooting

### Voice Does Not Respond

- Say `Computer` clearly, then the command.
- Try the push-to-talk fallback.
- Run `Headset Holodeck > Validate Install`.
- Confirm microphone permission.
- Confirm Sherpa model files are present.
- Confirm `OPENAI_API_KEY` is configured if using OpenAI-backed command interpretation.
- Make sure TTS is not currently speaking over the wake detector.

### The App Hears Its Own Voice

Wake detection should be suppressed while known TTS audio sources are playing. If this happens, check that the voice/TTS audio sources are assigned to the suppression list or discoverable by the voice activation system.

### Camera Capture Does Not Work

- In the Editor, approve macOS camera permission when prompted.
- On Quest, confirm the Android manifest includes camera/headset-camera permission.
- Say "capture image" first to open preview, then "OK", "shoot", or "capture" to save the frame.
- If no preview appears, check whether the device exposes a `WebCamTexture` camera to Unity.

### Image Search Buttons Are Disabled

- Add `PIXABAY_API_KEY` to `.env`.
- Reopen or refresh the panel.

### World Generation Does Not Start

- Confirm `WORLDLABS_API_KEY`.
- Confirm internet access.
- Check the World Labs status panel and Unity Console.
- Try a shorter prompt.

### Panorama Appears But No 3D Splat Loads

- The generated world may not have a usable splat URL.
- The download or runtime splat conversion may have failed.
- Try switching to panorama view.
- Try loading another saved/cached world.

### Local File Loading Fails

- Confirm the URL or path is reachable.
- Confirm the extension is supported.
- Prefer `.spz`; `.ply` support is more experimental.

### Requested Sounds Do Not Match The Scene

- Add `FREESOUND_API_KEY` for better general sound search.
- Add `XENO_CANTO_API_KEY` for bird/wildlife searches.
- Try specific sound names: "ocean waves", "seagulls", "forest birds", "crowd cheering".
- The app now tries to rewrite broad world prompts into better sound search terms, but third-party search quality still varies.

## Current Prototype Limitations

- This is still a local prototype, not a hardened consumer build.
- API credentials are handled locally. On a headset the build carries no keys: the app reads the
  `.env` you push to the device's own files directory. See the README's "Headset Run Flow".
- World generation and network downloads depend on third-party service availability.
- Object generation from image/text uses 3dAIStudio Tripo or HiTem and costs provider credits; it is
  disabled until one of those is configured. `MESHY_API_KEY` and `TRIPO_API_KEY` are reserved and
  enable nothing on their own.
- Multiplayer and avatars are planned future phases.
- Some voice commands depend on scene references and active hand/controller tracking.
- `.ply` loading is less mature than `.spz` loading.
- Delete actions are immediate.
- An interrupted save loses only the newest changes: startup recovers the previous generation from
  `world.json.bak`. The write path is verified on macOS and for backup recovery on Quest; the
  kill-mid-write case has not yet been reproduced on headset hardware.
- Frame timing has been measured only with the headset off (48 fps median), so worn-headset
  performance is not yet a verified number. SPZ decoding and floor analysis still run synchronously
  on the Unity main thread, which is what the measured 0.84–1.0 s cached-load stall consists of.
- A loose splat cannot be loaded by voice by bare filename (`load yoyo.spz` is treated as a
  saved-world name), and one mis-heard word in a saved-world name can defeat the lookup. Both are
  recorded findings with repro steps in `docs/stabilization/handoff-05.md`.

## Quick Command Examples

- "Computer, create a quiet redwood forest at dawn."
- "Computer, use high quality."
- "Computer, capture image."
- "Computer, OK."
- "Computer, create world from image, make it cinematic."
- "Computer, search images Japanese garden."
- "Computer, next image."
- "Computer, use this image."
- "Computer, capture thumbnail."
- "Computer, capture panorama."
- "Computer, show my worlds."
- "Computer, save as Redwood Dawn."
- "Computer, load Redwood Dawn."
- "Computer, add forest birds and wind in the trees."
- "Computer, stop all sounds."
- "Computer, create a ball in my left hand."
- "Computer, make the ball red metallic."
- "Computer, move me one meter forward."
- "Computer, delete this cube."
- "Computer, exit holodeck."
- "Computer, end program."
