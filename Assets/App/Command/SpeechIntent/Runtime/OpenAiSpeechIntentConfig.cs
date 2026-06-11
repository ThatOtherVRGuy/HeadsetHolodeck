using UnityEngine;

namespace SpeechIntent
{
    [CreateAssetMenu(fileName = "OpenAiSpeechIntentConfig", menuName = "Speech Intent/OpenAI Config")]
    public class OpenAiSpeechIntentConfig : ScriptableObject
    {
        [Header("Transport")]
        [Tooltip("Use your own backend/proxy in production. Direct OpenAI mode is for local prototypes only.")]
        public bool useProxyServer = false;

        [Tooltip("Your backend endpoint that accepts the audio file plus scene/spatial context and returns SpeechIntentResult JSON.")]
        public string proxyInterpretUrl = "http://localhost:8000/speech/interpret";

        [Tooltip("Only used when useProxyServer is false. Do not ship a production app with a raw OpenAI API key in the client.")]
        public string openAiApiKey = "";

        [Tooltip("Override only if you need a custom base URL.")]
        public string openAiBaseUrl = "https://api.openai.com/v1";

        [Header("Models")]
        [Tooltip("Fast speech-to-text model.")]
        public string transcriptionModel = "whisper-1";

        [Tooltip("BCP-47 language code passed to Whisper (e.g. 'en'). Leave empty to let Whisper auto-detect, but specifying the language improves accuracy and prevents mis-detection.")]
        public string transcriptionLanguage = "en";

        [Tooltip("Structured intent model. Verify the latest available model on the OpenAI dashboard.")]
        public string intentModel = "gpt-4o";

        [Header("Request Behavior")]
        [Range(0f, 2f)]
        public float intentTemperature = 0.1f;

        [Tooltip("When false, ask OpenAI not to store the request.")]
        public bool storeRequests = false;

        [Tooltip("Timeout in seconds for each HTTP request.")]
        public int timeoutSeconds = 60;

        [Header("Prompting")]
        [Tooltip("Domain-specific intent-routing hints appended after the structural schema rules. Edit here instead of modifying OpenAiSpeechIntentService.cs.")]
        [TextArea(12, 40)]
        public string additionalDeveloperInstructions =
@"For natural-language world requests like 'put me on a beach on a sunny day', use intent=GenerateWorld and create a concise world_prompt.
For 'exit holodeck', 'quit holodeck', 'close holodeck', 'exit the app', or 'quit the app', use intent=QuitApplication.
For 'end program', use intent=SwitchToStaticWorld.
For 'capture this', 'take a picture', 'capture what I am looking at', or 'use the headset camera', use intent=CaptureHeadsetCamera to open the live camera viewfinder.
When the live camera viewfinder is active and the user says 'OK', 'shoot', 'take it', 'capture', or 'capture now', use intent=ConfirmHeadsetCameraCapture.
For 'capture thumbnail', 'save thumbnail', 'update thumbnail', or 'make a thumbnail', use intent=CaptureWorldThumbnail. This captures the currently loaded virtual world for the My Worlds card, not the headset camera.
For 'capture panorama', 'capture pano', 'save panorama', or 'update panorama', use intent=CaptureWorldPanorama. This captures a virtual 360 panorama from the current position and saves it with the loaded world.
For 'search images for redwood forest', 'find a picture of neon Tokyo', or 'show me images of a castle', use intent=SearchImages and set image_search_query.
For 'use this image' or 'select this image' while the image search panel is active, use intent=SelectImageSearchResult.
For 'next image' or 'previous image' while browsing image search results, use intent=NextImageSearchResult or PreviousImageSearchResult.
For 'make a world inspired by this image', 'create a world from the captured image', or 'use this image and make it hyper realistic', use intent=GenerateWorldFromCapture. Put the user's descriptive guidance in world_prompt.
For 'make an object from this image', 'create an object from the captured image', or 'turn this image into a 3D object', use intent=GenerateObjectFromCapture. Put the user's descriptive guidance in object_name or world_prompt.
For 'arch', 'show arch', 'exit', or 'menu', use intent=ShowUi and ui_panel='arch_menu' unless context suggests another visible panel.
For 'hide arch', 'close arch', 'hide menu', or 'close menu', use intent=ShowUi and ui_panel='hide_arch'.
For 'show status', 'show loading', or 'show progress', use intent=ShowUi and ui_panel='status'.
For '3d', 'show 3d', or 'show splat', use intent=Show3dWorld.
For 'pano', 'panorama', or 'show panorama', use intent=ShowPanoWorld.
For 'put the sun there', use intent=SetSunDirection, target_entity='sun', and use the best available pointing-based spatial reference.
For placement phrases like 'put a cube there', 'place a tree over there', or 'put it where I am pointing', use a pointing-based spatial reference: prefer PointingHit for surface placement and PointingRay for direction/mid-air placement.
For object creation relative to the user, use intent=PlaceObject with spatial_reference=RelativeToMe. Example: 'create a teddy bear 1 meter in front of me' -> object_name='teddy bear', relative_direction=InFront, relative_distance_meters=1. Example: 'create a teddy bear at world origin' -> object_name='teddy bear', spatial_reference=WorldOrigin.
For plain object creation with no explicit location, default to one meter in front of the user: spatial_reference=RelativeToMe, relative_direction=InFront, relative_distance_meters=1. Do not ask 'Where?' for ordinary create/make object commands.
For object creation with attributes, keep the noun in object_name and put modifiers into fields. Examples: 'make a green cube' -> object_name='cube', material_prompt='green'. 'make a 2 meter wide cube' -> object_name='cube', object_width_meters=2. 'make a weightless sphere' -> object_name='sphere', object_weightless=true.
For target-relative creation such as 'create a cube to the left of the sphere', use spatial_reference=RelativeToTarget, target_name='sphere', relative_direction=Left, and placement_mode='me_frame', meaning left/right/front/back are relative to the user. For possessive wording such as 'create a cube to the sphere's left', use placement_mode='target_local', meaning the target object's local frame. Also support above/below as Up/Down.
Ask 'Where?' only when the user refers to an unresolved destination such as 'put it there' without usable pointing/spatial context, not for ordinary object creation.
For 'make it night time', use intent=SetLightingPreset with lighting_preset='night'.
For 'place tree here', use intent=PlaceObject, set object_name='tree', and use a spatial reference that matches the pointing context.
For phrases that refer to the user's body parts, use spatial_reference=BodyAnchor and body_anchor=Head, LeftHand, or RightHand. Examples: 'create a cube in my right hand' -> intent=PlaceObject, object_name='cube', body_anchor=RightHand, target_hand=Right. 'move the cube to my left hand' -> intent=MoveTarget, target_name='cube', body_anchor=LeftHand, target_hand=Left. 'add bird sounds in my right hand' -> intent=CreateAudioSource, sound_prompt='bird sounds', body_anchor=RightHand, target_hand=Right. Hands map to active hand tracking or controller transforms.
For 'make it bigger', 'make it twice as big', 'make it 10% smaller', or similar, use intent=ScaleTarget.
For pronouns like 'it' or 'that', prefer target_reference=LastCreatedOrInteracted unless the user is clearly pointing at a specific object right now.
For 'this' or 'that' target references in commands like 'delete this cube', 'make this larger', or 'move this 1 meter left of me', use target_reference=PointedObject when the wording implies gaze/hand indication. Keep target_name/object_name as the optional class label, e.g. 'cube'.
For material adjectives used to identify a target, put the adjective in target_material_prompt and the object class/name in target_name or object_name. Example: 'move the red cube up' -> intent=MoveTarget, target_name='cube', target_material_prompt='red'. Example: 'make the red metallic sphere smaller' -> intent=ScaleTarget, target_name='sphere', target_material_prompt='red metallic'.
For spatial ranking adjectives used to identify a target, put top/bottom ranking in target_spatial_qualifier. Supported values are 'topmost' and 'bottommost'. Example: 'delete the red sphere on top' -> intent=DeleteTarget, target_name='sphere', target_material_prompt='red', target_spatial_qualifier='topmost'.
When the user says the WORLD should change size or move, use target_reference=CurrentWorld.
For 'that is too big' or 'make it bigger' without a number, infer a reasonable multiplier. Use 0.8 for a modest reduction and 1.25 for a modest increase.
For 'make me normal size', 'reset my size', or 'make me default size', use intent=ScaleTarget, target_entity='Me', and reset_to_default_scale=true.
For 'reset me', use intent=ResetTransform and target_entity='Me'. Resets position to origin, rotation to zero, and scale to 1,1,1.
For 'rotate it by 45 degrees', use intent=RotateTarget, rotation_degrees=45, and default to rotation_axis=Y unless the utterance clearly indicates another axis.
For 'flip it upside down', use intent=RotateTarget with rotation_axis=X and rotation_degrees=180.
For 'move that here', use intent=MoveTarget and use the spatial reference to capture the destination.
For material/color/finish changes such as 'make it red', 'make this metallic blue', 'turn the cube red metallic', or 'make all red cubes matte black', use intent=SetTargetMaterial. Put the new color/finish phrase in material_prompt, such as 'red', 'blue metallic', or 'matte black'. If the target itself has a material adjective, put that in target_material_prompt, e.g. 'make all red cubes blue' -> target_reference=All, target_name='cube', target_material_prompt='red', material_prompt='blue'. Use target_reference=LastCreatedOrInteracted for 'it', PointedObject for 'this/that' when indicated, NamedObject for named objects, and All for 'all/every'.
For physics changes to existing objects, use intent=ModifyPhysics. Examples: 'make it weightless' -> target_reference=LastCreatedOrInteracted, physics_action='set_weightless', object_weightless=true. 'make the sphere weightless' -> target_reference=NamedObject, target_name='sphere', physics_action='set_weightless'. 'turn gravity back on' -> physics_action='enable_gravity'.
For 'move me to the origin' or 'move me to origin', use intent=MoveTarget, target_entity='Me', and spatial_reference=WorldOrigin.
For movement relative to the user/player, use spatial_reference=RelativeToMe. Example: 'move me 1 meter forward' targets Me, relative_direction=Forward, relative_distance_meters=1. Example: 'move the cube in front of me' targets the cube, relative_direction=InFront or Forward, and uses a reasonable distance if no distance is specified.
For relative directions, use Forward/Back/Left/Right/Up/Down/InFront/Behind from Me's local frame. 'my left' means Me's local left, not world left.
For delete/remove/destroy requests, use DeleteTarget. Examples: 'delete all audio' -> target_reference=All, target_name='audio'. 'delete all cubes' -> target_reference=All, target_name='cubes'. 'delete the cube' -> target_reference=NamedObject, target_name='cube'. Never map delete requests to world unload unless the user says end program.
Behavior commands:
- For requests to add temporary runtime behaviors to existing objects, use intent=AttachBehavior.
- Supported behavior_name values are spin, orbit, throw, follow_hand, and attach_to_hand.
- Examples: 'make the cube spin' -> behavior_name='spin', target_name='cube'. 'make the cube orbit the sun' -> behavior_name='orbit', target_name='cube', behavior_secondary_target_name='sun'. 'throw the ball' -> behavior_name='throw'. 'make the cube follow my left hand' -> behavior_name='follow_hand', target_hand=Left, body_anchor=LeftHand. 'attach the cube to my right hand' -> behavior_name='attach_to_hand', target_hand=Right, body_anchor=RightHand.
- Fill behavior_speed, behavior_radius, and behavior_axis when specified.
- For requests to stop/remove behaviors, use intent=StopBehavior. Examples: 'stop the cube spinning' -> behavior_name='spin', target_name='cube'. 'stop all behaviors' or 'remove all behaviors' -> behavior_stop_all=true and target_reference=All. 'stop this following my hand' -> target_reference=PointedObject, behavior_name='follow_hand'.
- Do not map behavior commands to one-time transform/material/light/audio edits unless the user asks for a direct one-time edit.
If the user says only 'rotate it' with no useful default amount, ask a clarification question like 'By how many degrees would you like to rotate it?'.
For 'use high quality', 'best model', 'use draft', 'switch to fast model', 'use standard', etc., use intent=SetGenerationModel. Set generation_model to exactly one of: draft, fast, standard, high. Accept 'low' as an alias for 'fast' and 'best' or 'premium' as aliases for 'high'. Accept 'normal' as an alias for 'standard'.
For 'show mesh', 'mesh view', 'show collision mesh', 'show 3d mesh', or 'show the mesh', use intent=ShowMeshWorld.
SaveWorldConfig: Use when user says 'save', 'save my world', or 'save as [name]'. Populate config_name with the name after 'save as', empty string for plain 'save'.
LoadWorldConfig: Use when user says 'load', 'open my worlds', 'show my worlds', or 'load [name]'. Populate config_name with the name after 'load', empty string to open the panel.
Audio commands:
- If the user asks to add, create, download, generate, place, or get a new sound, use intent=CreateAudioSource. Do not change world_prompt and do not change the visual world. For 'quiet' sounds, set audio_volume=0.25 unless another value is specified.
- If the user says 'play [name]' or 'play [name] sounds', assume [name] refers to an existing audio source. Use intent=ControlAudioSource, audio_control='play_now', audio_play_now=true, and target_name or sound_prompt set to the named sound. Do not create/download a new sound unless the user clearly asks for a new one.
- For collective sound requests, split layers into sound_prompts. Example: 'add birds in trees and river rapids' -> sound_prompts=['birds in trees','river rapids'], sound_count=2.
- Use audio_playback_mode=loop for continuous background ambience such as ocean, rain, river, wind, crowds, traffic, machinery, hum, or general ambience.
- Use audio_playback_mode=once for one-shot effects such as knocks, doors, impacts, button clicks, footsteps, crashes, or explosions.
- Use audio_playback_mode=random_interval for natural sparse events such as birds, frogs, crickets, insects, thunder, bells, chimes, or animal calls.
- If the user says 'play every X seconds', use audio_playback_mode=interval and set audio_interval_seconds=X.
- If the user says 'play at random intervals' or includes a variance, use audio_playback_mode=random_interval, set audio_interval_seconds to the base interval if provided, and set audio_interval_variance_seconds to the variance if provided.
- For bird species or birdsong, prefer sound_provider='xeno-canto' when appropriate; otherwise use sound_provider='auto'.
- If the user asks to change an existing sound, use intent=ControlAudioSource. Use target_name or sound_prompt for the named sound, and target_reference=LastCreatedOrInteracted for pronouns like 'it'.
- If the user targets every sound, for example 'stop all sounds', 'mute all sounds', or 'make all audio quieter', use intent=ControlAudioSource, target_reference=All, target_name='all'. This applies to world/program audio, not UX audio such as TTS or cues.
- For 'stop [sound]' or 'stop all sounds', use audio_control='stop'. Stop playback but do not destroy the audio source.
- For 'make it louder', use audio_control='louder' and audio_volume_delta=0.15 unless the user specifies an amount.
- For 'make it softer' or 'make it quieter', use audio_control='softer' and audio_volume_delta=-0.15 unless the user specifies an amount.
- For 'mute it' and 'unmute it', use audio_control='mute' or audio_control='unmute'.
- For 'make it ambient' or 'make it 2D', use audio_control='make_ambient' and audio_spatial_blend=0.
- For 'make it spatialized' or 'make it 3D', use audio_control='make_spatialized' and audio_spatial_blend=1.
- For 'play [name] now', use intent=ControlAudioSource, audio_control='play_now', audio_play_now=true, and target_name or sound_prompt set to the named sound.";
    }
}
