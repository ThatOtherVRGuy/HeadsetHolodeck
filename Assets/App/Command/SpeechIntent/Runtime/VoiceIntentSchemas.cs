using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SpeechIntent
{
    public enum VoiceIntentType
    {
        Unknown = 0,
        AskClarification = 1,
        GenerateWorld = 2,
        SwitchToStaticWorld = 3,
        ShowUi = 4,
        SetSunDirection = 5,
        SetLightingPreset = 6,
        PlaceObject = 7,
        MoveTarget = 8,
        ScaleTarget = 9,
        RotateTarget = 10,
        Show3dWorld     = 11,
        ShowPanoWorld   = 12,
        ResetTransform  = 13,
        LoadSplat       = 14,  // load a local/remote .spz or .ply file
        LoadPanorama    = 15,  // load a local/remote panoramic image
        SetGenerationModel = 16,  // change the active generation model tier
        ShowMeshWorld      = 17,  // switch view to the collision mesh
        SaveWorldConfig    = 18,  // "save" or "save as [name]"
        LoadWorldConfig    = 19,  // "load [name]" or "show my worlds"
        CreateAudioSource  = 20,  // add one or more prompt-matched audio sources without changing graphics
        ControlAudioSource = 21,  // change playback/volume/spatial settings on an existing audio source
        QuitApplication    = 22,  // "exit holodeck" / quit the app
        CaptureHeadsetCamera = 23,  // capture the device passthrough/headset camera for preview
        GenerateWorldFromCapture = 24,  // use the approved captured camera image plus text as a world prompt
        GenerateObjectFromCapture = 25,  // use the approved captured camera image plus text as an object prompt
        ConfirmHeadsetCameraCapture = 26,  // approve the active live camera preview and save the current frame
        SearchImages = 27,  // search an online image provider such as Pixabay
        SelectImageSearchResult = 28,  // choose/use the currently selected online image as the image prompt
        NextImageSearchResult = 29,  // advance to the next online image search result
        PreviousImageSearchResult = 30,  // go back to the previous online image search result
        DeleteTarget = 31,  // delete/remove an existing object, category, or world audio target
        CaptureWorldThumbnail = 32,  // capture the current rendered world view as this world's card thumbnail
        CaptureWorldPanorama = 33,  // capture a 360 panorama from the current view and store it in this world's folder
        SetTargetMaterial = 34,  // change material/color/finish on an existing object or object group
        CreateLight = 35,  // create a runtime point/spot/directional light or adjust ambient scene light
        ModifyLight = 36,  // change color/intensity/range/cone/sun role for one or more runtime lights
        SetProxyVisibility = 37,  // show/hide visual proxies for runtime lights, audio, or all proxy-enabled entities
        SelectCachedObject = 38,  // reply to a saved-vs-new cached object choice
        CancelGeneration = 39,  // cancel a pending object/world generation job
        ContinueGeneration = 40,  // acknowledge a long-running generation job and keep waiting
        AttachBehavior = 41,  // attach a curated runtime behavior such as spin, orbit, throw, or follow hand
        StopBehavior = 42,  // stop one named behavior, behaviors on a target, or all runtime behaviors
        ModifyPhysics = 43,  // change Rigidbody/gravity/mass properties on an existing object
        SaveSpawnPoint = 44,  // save the current Me/XR origin pose into this world's spawn point list
        NextSpawnPoint = 45,  // move Me to the next saved spawn point for the current world
        PreviousSpawnPoint = 46,  // move Me to the previous saved spawn point for the current world
        RemoveSpawnPoint = 47,  // remove the current/last-used spawn point from this world's spawn point list
        RemoveAllSpawnPoints = 48,  // remove every saved spawn point from this world's spawn point list
        SuggestSpawnPoint = 49,  // move Me to the current world's estimated spawn pose without saving it
    }

    [Serializable]
    public enum SpatialReferenceMode
    {
        None = 0,
        PointingRay = 1,
        PointingHit = 2,
        HandMidpoint = 3,
        HeadForward = 4,
        WorldOrigin = 5,
        RelativeToMe = 6,
        BodyAnchor = 7,
        RelativeToTarget = 8
    }

    [Serializable]
    public enum HandSelection
    {
        None = 0,
        Left = 1,
        Right = 2,
        Either = 3,
        Both = 4
    }

    [Serializable]
    public enum BodyAnchor
    {
        None = 0,
        Head = 1,
        LeftHand = 2,
        RightHand = 3
    }

    [Serializable]
    public enum TargetReferenceMode
    {
        None = 0,
        CurrentWorld = 1,
        LastCreatedObject = 2,
        LastInteractedTarget = 3,
        LastCreatedOrInteracted = 4,
        PointedObject = 5,
        NamedObject = 6,
        CurrentSelection = 7,
        All = 8
    }

    [Serializable]
    public enum RotationAxis
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 3
    }

    [Serializable]
    public enum RelativeDirection
    {
        None = 0,
        Forward = 1,
        Back = 2,
        Left = 3,
        Right = 4,
        Up = 5,
        Down = 6,
        InFront = 7,
        Behind = 8
    }

    [Serializable]
    public class HandRaySnapshot
    {
        public string source_name = "";
        public bool is_available;
        public bool is_pointing;
        public float pointing_confidence;
        public float distance_from_head;
        public float forward_alignment;
        public Vector3 origin;
        public Vector3 direction;
        public bool has_hit;
        public Vector3 hit_point;
        public Vector3 hit_normal;
        public string hit_object_name;
        public string hit_object_path;
        public string hit_root_name;
    }

    [Serializable]
    public class SpatialSnapshot
    {
        public HandRaySnapshot left_hand = new HandRaySnapshot();
        public HandRaySnapshot right_hand = new HandRaySnapshot();
        public bool has_hand_midpoint;
        public Vector3 hand_midpoint;
        public Vector3 head_position;
        public Vector3 head_forward;
    }

    [Serializable]
    public class SceneSemanticSnapshot
    {
        public string current_world_description = "";
        public string current_world_root_name = "";
        public string current_lighting_preset = "";
        public bool static_world_active;
        public string last_created_target_name = "";
        public string last_interacted_target_name = "";
        public List<string> available_placeable_objects = new List<string>();
        public List<string> visible_ui_panels = new List<string>();
        public List<string> aliases_for_ui = new List<string>();
        public List<string> named_scene_objects = new List<string>();
    }

    [Serializable]
    public class VoiceIntentCommand
    {
        public string transcript = "";
        public VoiceIntentType intent = VoiceIntentType.Unknown;
        public float confidence = 0f;
        public bool should_execute = false;

        [Tooltip("Optional acknowledgement or clarification message for TTS/UI.")]
        public string spoken_response = "";

        [Header("World Generation")]
        public string world_prompt = "";

        [Header("Image Search")]
        [Tooltip("Search query for SearchImages, e.g. 'redwood forest' or 'neon Tokyo street'.")]
        public string image_search_query = "";

        [Header("UI")]
        public string ui_panel = "";

        [Header("Lighting / Sun")]
        public string target_entity = "";
        public string lighting_preset = "";
        [Tooltip("point, spot, directional, ambient, or flashlight. Flashlight creates a tight-cone spot light.")]
        public string light_type = "";
        [Tooltip("Color phrase for light creation/modification, e.g. yellow, warm white, red.")]
        public string light_color_prompt = "";
        [Tooltip("brighter, dimmer, redder, greener, bluer, warmer, cooler, set_color, set_intensity, set_range, set_spot_angle, set_sun.")]
        public string light_action = "";
        public float light_intensity = 0f;
        public float light_range = 0f;
        public float light_spot_angle = 0f;
        public HandSelection target_hand = HandSelection.None;
        public SpatialReferenceMode spatial_reference = SpatialReferenceMode.None;
        [Tooltip("Named user body anchor for spatial_reference=BodyAnchor. Head uses Main Camera/head; hands use active hand/controller sources.")]
        public BodyAnchor body_anchor = BodyAnchor.None;

        [Header("Placement")]
        public string object_name = "";
        public string placement_mode = "surface";
        [Tooltip("Desired created-object width in meters. Zero means keep default/imported size.")]
        public float object_width_meters = 0f;
        [Tooltip("When true, created object should not fall under gravity.")]
        public bool object_weightless = false;
        [Tooltip("use_saved, create_new, or cancel for SelectCachedObject.")]
        public string object_choice_action = "";

        [Header("Target Resolution")]
        public TargetReferenceMode target_reference = TargetReferenceMode.None;
        public string target_name = "";
        [Tooltip("Optional material/color/finish qualifier for target resolution, e.g. 'red' in 'the red cube'.")]
        public string target_material_prompt = "";
        [Tooltip("Optional spatial ranking qualifier for target resolution. Supported values: topmost, bottommost.")]
        public string target_spatial_qualifier = "";

        [Header("Transform Commands")]
        public float scale_multiplier = 1f;
        public bool reset_to_default_scale = false;
        public RotationAxis rotation_axis = RotationAxis.None;
        public float rotation_degrees = 0f;
        [Tooltip("Direction relative to Me / XR Origin when spatial_reference=RelativeToMe.")]
        public RelativeDirection relative_direction = RelativeDirection.None;
        [Tooltip("Distance in meters for relative movement. Zero uses TargetTransformController.defaultRelativeToMeDistance.")]
        public float relative_distance_meters = 0f;

        [Header("Material Commands")]
        [Tooltip("New material/color/finish to apply, such as 'red', 'red metallic', or 'matte black'.")]
        public string material_prompt = "";

        [Header("Physics Commands")]
        [Tooltip("set_weightless, enable_gravity, disable_gravity, or set_mass.")]
        public string physics_action = "";
        [Tooltip("Optional mass value for set_mass. Zero means not specified.")]
        public float physics_mass = 0f;

        [Header("Runtime Proxies")]
        [Tooltip("all, light, audio, or a future proxy category.")]
        public string proxy_category = "";
        public bool proxy_visible = false;

        [Header("Runtime Behaviors")]
        [Tooltip("Curated behavior name such as spin, orbit, throw, follow_hand, or attach_to_hand.")]
        public string behavior_name = "";
        [Tooltip("start, stop, toggle, or a behavior-specific action.")]
        public string behavior_action = "";
        [Tooltip("Optional secondary target for behaviors such as orbit or throw.")]
        public string behavior_secondary_target_name = "";
        [Tooltip("Speed parameter for behavior commands. Units depend on behavior: degrees/second for spin/orbit, meters/second for throw.")]
        public float behavior_speed = 0f;
        [Tooltip("Radius in meters for orbit-style behaviors.")]
        public float behavior_radius = 0f;
        [Tooltip("Axis hint such as x, y, z, up, right, forward, local_up, or world_up.")]
        public string behavior_axis = "";
        [Tooltip("When true, stop every runtime behavior in the scene.")]
        public bool behavior_stop_all = false;

        [Header("World Generation Model")]
        [Tooltip("Target model tier for SetGenerationModel intent. Values: draft, fast, standard, high.")]
        public string generation_model = "";

        [Header("Local/Remote Content")]
        [Tooltip("File name, relative path, or full URL for LoadSplat and LoadPanorama intents.")]
        public string content_path = "";

        [Header("World Config")]
        [Tooltip("Config name for SaveWorldConfig (save-as) and LoadWorldConfig (load by name). Empty = save current / open panel.")]
        public string config_name = "";

        [Header("Prompted Audio")]
        [Tooltip("Primary sound request, e.g. 'river rapids' or 'birds in trees'.")]
        public string sound_prompt = "";
        [Tooltip("Optional multiple sound layers. Use for collective sounds such as birds, river, wind.")]
        public List<string> sound_prompts = new List<string>();
        [Tooltip("Optional category such as ambience, birds, water, weather, machinery, crowd.")]
        public string sound_category = "";
        [Tooltip("Optional species for bird/wildlife search.")]
        public string sound_species = "";
        [Tooltip("auto, freesound, openverse, or xeno-canto.")]
        public string sound_provider = "";
        [Tooltip("Maximum number of separate audio sources to instantiate from this command.")]
        public int sound_count = 1;
        [Tooltip("Maximum desired clip length in seconds.")]
        public float sound_max_duration_seconds = 45f;
        public bool audio_loop = true;
        public float audio_volume = 1f;
        [Tooltip("Relative volume change. Positive = louder, negative = quieter.")]
        public float audio_volume_delta = 0f;
        [Tooltip("auto, loop, once, interval, or random_interval.")]
        public string audio_playback_mode = "auto";
        [Tooltip("Base interval in seconds for interval/random_interval playback.")]
        public float audio_interval_seconds = 0f;
        [Tooltip("Random plus/minus variance in seconds around audio_interval_seconds.")]
        public float audio_interval_variance_seconds = 0f;
        [Tooltip("stop, mute, unmute, louder, softer, set_volume, set_interval, set_random_interval, make_ambient, make_spatialized, play_now.")]
        public string audio_control = "";
        public bool audio_muted = false;
        public bool audio_play_now = false;
        public float audio_spatial_blend = 1f;

        [Header("Diagnostics")]
        public string reason = "";
    }

    [Serializable]
    public class SpeechIntentResult
    {
        public bool success;
        public string transcript = "";
        public VoiceIntentCommand command = new VoiceIntentCommand();
        public string raw_model_json = "";
        public string error = "";
    }

    [Serializable]
    public class StringEvent : UnityEvent<string> { }

    [Serializable]
    public class VoiceIntentCommandEvent : UnityEvent<VoiceIntentCommand> { }

    [Serializable]
    public class SpeechIntentResultEvent : UnityEvent<SpeechIntentResult> { }

    [Serializable]
    public class NamedPrefabEntry
    {
        public string name;
        public GameObject prefab;
    }

    [Serializable]
    internal class TranscriptionResponse
    {
        public string text;
    }
}
