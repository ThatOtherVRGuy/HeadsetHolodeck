using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WorldLabs.Runtime;
using WorldLabs.Runtime.Tools;
using Holodeck.Direct;
using Holodeck.Save;
using Holodeck.State;
using SpeechIntent;
using SpeechIntent.Audio;

namespace Holodeck.Editor
{
    /// <summary>
    /// Editor utility that adds and wires the SpeechIntent system into the current scene.
    ///
    /// Creates a "SpeechIntent" child under "Systems" (both created if missing), adds all
    /// required components, wires their internal references, and connects the two cross-system
    /// UnityEvents:
    ///   WorldActionDispatcher.onGenerateWorldPrompt  → coordinator.TriggerWorldGeneration  (Dynamic String)
    ///   WorldActionDispatcher.onSwitchToStaticWorld  → worldManager.RestoreDefaultWorld    (Static)
    ///
    /// Re-running is safe — existing components are reused and references are re-wired.
    ///
    /// Menu: Holodeck > Setup SpeechIntent
    /// </summary>
    public static class SpeechIntentSceneSetup
    {
        private const string ConfigAssetPath =
            "Assets/App/Command/SpeechIntent/OpenAiSpeechIntentConfig.asset";

        private const string InputActionsPath =
            "Assets/App/Input/HolodeckInputActions.inputactions";

        [MenuItem("Holodeck/Setup SpeechIntent")]
        public static void SetupSpeechIntent()
        {
            // ── 1. Find or create scene hierarchy ─────────────────────────────
            GameObject systems      = EnsureRootObject("Systems");
            GameObject speechRoot   = EnsureChildObject(systems, "SpeechIntent");

            // ── 2. Add SpeechIntent components ────────────────────────────────
            MicrophoneWavRecorder          recorder      = GetOrAdd<MicrophoneWavRecorder>(speechRoot);
            SpatialContextProvider         spatial       = GetOrAdd<SpatialContextProvider>(speechRoot);
            SceneSemanticContextProvider   semantic      = GetOrAdd<SceneSemanticContextProvider>(speechRoot);
            OpenAiSpeechIntentService      service       = GetOrAdd<OpenAiSpeechIntentService>(speechRoot);
            WorldActionDispatcher          dispatcher    = GetOrAdd<WorldActionDispatcher>(speechRoot);
            InteractionMemory              memory        = GetOrAdd<InteractionMemory>(speechRoot);
            VoiceCommandRouter             router        = GetOrAdd<VoiceCommandRouter>(speechRoot);
            PushToTalkTrigger              trigger       = GetOrAdd<PushToTalkTrigger>(speechRoot);
            SceneEntityResolver            entityResolver  = GetOrAdd<SceneEntityResolver>(speechRoot);
            TargetTransformController      targetTransform = GetOrAdd<TargetTransformController>(speechRoot);
            LightRigController             lightRig           = GetOrAdd<LightRigController>(speechRoot);
            UiPanelController              uiPanels           = GetOrAdd<UiPanelController>(speechRoot);
            StaticWorldController          staticWorld        = GetOrAdd<StaticWorldController>(speechRoot);
            ViewModeController             viewMode           = GetOrAdd<ViewModeController>(speechRoot);
            PlayerOriginController         playerOrigin       = GetOrAdd<PlayerOriginController>(speechRoot);
            LocalRemoteSplatLoader         splatLoader        = GetOrAdd<LocalRemoteSplatLoader>(speechRoot);
            LocalRemotePanoLoader          panoLoader         = GetOrAdd<LocalRemotePanoLoader>(speechRoot);
            FreesoundProvider              freesoundProvider  = GetOrAdd<FreesoundProvider>(speechRoot);
            OpenverseProvider              openverseProvider  = GetOrAdd<OpenverseProvider>(speechRoot);
            XenoCantoSoundProvider         xenoCantoProvider  = GetOrAdd<XenoCantoSoundProvider>(speechRoot);
            AudioWorldActionController     audioController    = GetOrAdd<AudioWorldActionController>(speechRoot);
            SpeechIntent.Behaviors.BehaviorCommandController behaviorController =
                GetOrAdd<SpeechIntent.Behaviors.BehaviorCommandController>(speechRoot);

            // ── 3. Locate cross-system dependencies ───────────────────────────
            WorldLabsWorldManager worldManager = systems.GetComponentInChildren<WorldLabsWorldManager>(true);
            VoiceToWorldLabsPluginCoordinator coordinator =
                systems.GetComponentInChildren<VoiceToWorldLabsPluginCoordinator>(true);
            WorldConfigStore worldConfigStore = speechRoot.GetComponent<WorldConfigStore>();
            WorldConfigAutoSave worldConfigAutoSave = speechRoot.GetComponent<WorldConfigAutoSave>();

            if (worldManager == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] WorldLabsWorldManager not found under Systems. " +
                                 "Run Holodeck > Setup Holodeck Scene first, then re-run this.");

            if (coordinator == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] VoiceToWorldLabsPluginCoordinator not found under Systems. " +
                                 "Run Holodeck > Setup Holodeck Scene first, then re-run this.");

            // ── 4. Ensure config ScriptableObject exists ──────────────────────
            OpenAiSpeechIntentConfig config = EnsureConfigAsset();

            // ── 5. Wire internal SpeechIntent references ──────────────────────
            Undo.RecordObjects(
                new Object[] { service, dispatcher, memory, router, trigger, semantic, entityResolver, targetTransform, lightRig, uiPanels, staticWorld, playerOrigin, splatLoader, panoLoader, audioController, behaviorController },
                "Wire SpeechIntent Components");

            service.config = config;

            semantic.interactionMemory    = memory;
            semantic.entityResolver       = entityResolver;

            entityResolver.interactionMemory = memory;

            dispatcher.interactionMemory         = memory;
            dispatcher.targetTransformController = targetTransform;
            dispatcher.lightRig              = lightRig;
            dispatcher.uiPanels              = uiPanels;
            dispatcher.staticWorldController = staticWorld;
            dispatcher.viewModeController      = viewMode;
            dispatcher.playerOriginController  = playerOrigin;
            dispatcher.splatLoader = splatLoader;
            dispatcher.panoLoader  = panoLoader;
            dispatcher.audioWorldActionController = audioController;
            dispatcher.behaviorCommandController = behaviorController;

            audioController.freesoundProvider = freesoundProvider;
            audioController.openverseProvider = openverseProvider;
            audioController.xenoCantoProvider = xenoCantoProvider;
            audioController.worldConfigStore = worldConfigStore;
            audioController.worldConfigAutoSave = worldConfigAutoSave;
            audioController.worldManager = worldManager;
            audioController.interactionMemory = memory;

            router.recorder               = recorder;
            router.spatialContextProvider = spatial;
            router.sceneContextProvider   = semantic;
            router.speechIntentService    = service;
            router.dispatcher             = dispatcher;

            trigger.router = router;

            targetTransform.entityResolver    = entityResolver;
            targetTransform.interactionMemory = memory;

            behaviorController.entityResolver = entityResolver;
            behaviorController.interactionMemory = memory;
            behaviorController.spatialContextProvider = spatial;

            // Wire PushToTalkTrigger.pushToTalkAction — find the InputActionReference sub-asset
            // that Unity's Input System bakes into the .inputactions file on import.
            // Using a sub-asset gives a stable GUID-backed reference that survives domain reload.
            Object[] inputSubAssets = AssetDatabase.LoadAllAssetsAtPath(InputActionsPath);
            InputActionReference wakeRef = null;
            foreach (Object a in inputSubAssets)
            {
                if (a is InputActionReference r && r.action?.name == "WakeCommand")
                {
                    wakeRef = r;
                    break;
                }
            }

            if (wakeRef != null)
            {
                SerializedObject triggerSo = new SerializedObject(trigger);
                triggerSo.FindProperty("pushToTalkAction").objectReferenceValue = wakeRef;
                triggerSo.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("[SpeechIntentSceneSetup] WakeCommand InputActionReference sub-asset not found in " +
                                 InputActionsPath + ". Assign PushToTalkTrigger.pushToTalkAction manually in the Inspector.");
            }

            // Wire InteractionMemory.worldManager (private [SerializeField])
            if (worldManager != null)
            {
                SerializedObject memSo = new SerializedObject(memory);
                memSo.FindProperty("worldManager").objectReferenceValue = worldManager;
                memSo.ApplyModifiedProperties();
            }

            // ── Scene reference wiring ────────────────────────────────────────
            GameObject dirLightGo = GameObject.Find("Lighting/DirectionalLight");
            if (dirLightGo != null)
            {
                Undo.RecordObject(lightRig, "Wire LightRigController");
                lightRig.sunLight = dirLightGo.GetComponent<Light>();
            }
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] 'Lighting/DirectionalLight' not found. Assign lightRig.sunLight manually.");

            GameObject staticRoot  = GameObject.Find("Environment/TNGHolodeck");
            GameObject dynamicRoot = GameObject.Find("Environment/GeneratedWorldRoot");
            Undo.RecordObject(staticWorld, "Wire StaticWorldController");
            if (staticRoot != null)
                staticWorld.staticWorldRoot = staticRoot;
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] 'Environment/TNGHolodeck' not found. Assign staticWorldController.staticWorldRoot manually.");
            if (dynamicRoot != null)
                staticWorld.dynamicWorldRoot = dynamicRoot;
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] 'Environment/GeneratedWorldRoot' not found. Assign staticWorldController.dynamicWorldRoot manually.");

            Undo.RecordObject(uiPanels, "Wire UiPanelController panels");
            WireUiPanel(uiPanels, "arch_menu", "UI/WorldLabs_GUI");
            WireUiPanel(uiPanels, "status",    "UI/WorldLabs_Status");

            Undo.RecordObject(viewMode, "Wire ViewModeController");
            viewMode.thumbnailSkybox   = systems.GetComponentInChildren<ThumbnailSkyboxController>(true);
            viewMode.interactionMemory = memory;
            viewMode.worldManager      = worldManager;
            viewMode.stateMachine      = systems.GetComponentInChildren<HolodeckStateMachine>(true);
            viewMode.coordinator       = coordinator;
            GameObject worldLabsGui = GameObject.Find("UI/WorldLabs_GUI");
            viewMode.worldBrowser = worldLabsGui != null ? worldLabsGui.GetComponent<WorldBrowserController>() : null;
            if (viewMode.worldBrowser == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] WorldBrowserController not found at 'UI/WorldLabs_GUI'. Assign viewMode.worldBrowser manually.");

            if (viewMode.thumbnailSkybox == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] ThumbnailSkyboxController not found under Systems.");
            if (viewMode.worldManager == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] WorldLabsWorldManager not found under Systems.");
            if (viewMode.stateMachine == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] HolodeckStateMachine not found under Systems.");

            Undo.RecordObject(playerOrigin, "Wire PlayerOriginController");
            playerOrigin.thumbnailSkybox = systems.GetComponentInChildren<ThumbnailSkyboxController>(true);
            playerOrigin.worldManager    = worldManager;
            playerOrigin.worldConfigAutoSave = worldConfigAutoSave;
            GameObject meGo = GameObject.Find("Me");
            if (meGo != null)
                playerOrigin.playerRoot = meGo.transform;
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] GameObject named 'Me' not found. Assign playerOrigin.playerRoot manually.");

            GameObject teleportAnchorGo = GameObject.Find("Teleport Anchor");
            if (teleportAnchorGo != null)
                playerOrigin.resetAnchor = teleportAnchorGo.transform;
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] GameObject named 'Teleport Anchor' not found. PlayerOriginController will fall back to world origin.");

            Undo.RecordObject(splatLoader, "Wire LocalRemoteSplatLoader");
            splatLoader.worldManager = worldManager;
            RuntimeSplatFloorLoader floorLoader =
                systems.GetComponentInChildren<RuntimeSplatFloorLoader>(true);
            if (floorLoader != null)
            {
                ConfigureRuntimeSplatFloorLoader(floorLoader);
                splatLoader.floorLoader = floorLoader;
            }
            else
                Debug.LogWarning("[SpeechIntentSceneSetup] RuntimeSplatFloorLoader not found under Systems. " +
                                 "Assign splatLoader.floorLoader manually.");

            Undo.RecordObject(panoLoader, "Wire LocalRemotePanoLoader");
            panoLoader.thumbnailSkybox    = systems.GetComponentInChildren<ThumbnailSkyboxController>(true);
            panoLoader.viewModeController = viewMode;

            // ── Wire SpatialContextProvider (head + hands) ────────────────────
            WireSpatialContextProvider(spatial, meGo);

            // ── 6. Wire cross-system UnityEvents ──────────────────────────────
            if (coordinator != null)
                WireGenerateWorldEvent(dispatcher, coordinator);

            if (worldManager != null)
                WireStaticWorldEvent(dispatcher, worldManager);

            // ── 7. Mark dirty ────────────────────────────────────────────────��
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[SpeechIntentSceneSetup] Done. " +
                      "Set your OpenAI API key in the OpenAiSpeechIntentConfig asset at:\n" +
                      ConfigAssetPath);
        }

        // ── Event wiring ──────────────────────────────────────────────────────

        /// <summary>
        /// Wires dispatcher.onGenerateWorldPrompt → coordinator.TriggerWorldGeneration
        /// in Dynamic String mode (the string from the event is passed through at runtime).
        /// Skips if the listener is already registered.
        /// </summary>
        private static void WireGenerateWorldEvent(
            WorldActionDispatcher dispatcher,
            VoiceToWorldLabsPluginCoordinator coordinator)
        {
            var evt = dispatcher.onGenerateWorldPrompt;

            // Check if already wired to avoid duplicates on re-run.
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (evt.GetPersistentTarget(i) == (Object)coordinator &&
                    evt.GetPersistentMethodName(i) == nameof(VoiceToWorldLabsPluginCoordinator.TriggerWorldGeneration))
                    return;
            }

            Undo.RecordObject(dispatcher, "Wire onGenerateWorldPrompt");
            UnityEventTools.AddPersistentListener(evt, coordinator.TriggerWorldGeneration);
            EditorUtility.SetDirty(dispatcher);
        }

        /// <summary>
        /// Wires dispatcher.onSwitchToStaticWorld → worldManager.RestoreDefaultWorld (Static/Void).
        /// Skips if already registered.
        /// </summary>
        private static void WireStaticWorldEvent(
            WorldActionDispatcher dispatcher,
            WorldLabsWorldManager worldManager)
        {
            var evt = dispatcher.onSwitchToStaticWorld;

            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (evt.GetPersistentTarget(i) == (Object)worldManager &&
                    evt.GetPersistentMethodName(i) == nameof(WorldLabsWorldManager.RestoreDefaultWorld))
                    return;
            }

            Undo.RecordObject(dispatcher, "Wire onSwitchToStaticWorld");
            UnityEventTools.AddVoidPersistentListener(evt, worldManager.RestoreDefaultWorld);
            EditorUtility.SetDirty(dispatcher);
        }

        // ── Config asset ───────────────────────────────────────────────���──────

        private static OpenAiSpeechIntentConfig EnsureConfigAsset()
        {
            OpenAiSpeechIntentConfig existing =
                AssetDatabase.LoadAssetAtPath<OpenAiSpeechIntentConfig>(ConfigAssetPath);

            if (existing != null)
                return existing;

            // Create the directory if needed.
            string dir = System.IO.Path.GetDirectoryName(ConfigAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
                System.IO.Directory.CreateDirectory(dir);

            OpenAiSpeechIntentConfig asset = ScriptableObject.CreateInstance<OpenAiSpeechIntentConfig>();
            AssetDatabase.CreateAsset(asset, ConfigAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SpeechIntentSceneSetup] Created OpenAiSpeechIntentConfig at {ConfigAssetPath}. " +
                      "Set your openAiApiKey before entering Play mode.");

            return asset;
        }

        // ── Scene helpers ─────────────────────────────────────────────────────

        private static void ConfigureRuntimeSplatFloorLoader(RuntimeSplatFloorLoader floorLoader)
        {
            Undo.RecordObject(floorLoader, "Configure RuntimeSplatFloorLoader");

            floorLoader.defaultSourceKind = RuntimeSplatFloorLoader.SplatSourceKind.WorldLabs;
            floorLoader.looseSplatMirrorAxis = RuntimeSplatFloorLoader.MirrorAxis.Z;
            floorLoader.autoPlaceAtOrigin = true;
            floorLoader.attachEstimatedSpawnPose = true;
            floorLoader.spawnEyeHeightMeters = 1.6f;
            floorLoader.spawnLookDistanceMeters = 2f;
            floorLoader.spawnEstimation ??= new SplatSpawnEstimatorSettings();
            floorLoader.spawnEstimation.radialCaptureOpenSideOffsetFraction = 0.35f;
            floorLoader.spawnEstimation.radialCaptureMaxOpenSideOffsetMeters = 2f;
            floorLoader.spawnEstimation.preferNormalConsensusAsSpawn = true;
            floorLoader.spawnEstimation.preferLongAxisConsensusAsSpawn = true;
            floorLoader.spawnEstimation.preferOriginFallbackOverCandidateSearch = true;
            floorLoader.spawnEstimation.refineSpawnYFromLocalFloor = true;
            floorLoader.spawnEstimation.localFloorSearchRadiusFractionOfDiagonal = 0.06f;
            floorLoader.spawnEstimation.localFloorMinRadiusMeters = 0.5f;
            floorLoader.spawnEstimation.localFloorMaxHeightFraction = 0.5f;
            floorLoader.spawnEstimation.localFloorPercentile = 0.08f;
            floorLoader.spawnEstimation.minLocalFloorSamples = 24;

            EditorUtility.SetDirty(floorLoader);
        }

        private static GameObject EnsureRootObject(string objName)
        {
            Scene active = SceneManager.GetActiveScene();
            foreach (GameObject root in active.GetRootGameObjects())
            {
                if (root.name == objName)
                    return root;
            }

            GameObject created = new GameObject(objName);
            Undo.RegisterCreatedObjectUndo(created, $"Create {objName}");
            return created;
        }

        private static GameObject EnsureChildObject(GameObject parent, string childName)
        {
            Transform existing = parent.transform.Find(childName);
            if (existing != null)
                return existing.gameObject;

            GameObject child = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(child, $"Create {childName}");
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            if (existing != null)
                return existing;

            return Undo.AddComponent<T>(go);
        }

        /// <summary>
        /// Discovers the XR rig head camera and controller transforms under <paramref name="xrRig"/>,
        /// adds PointingSource components where needed, and wires SpatialContextProvider.
        ///
        /// Searched controller name fragments (case-insensitive): "left controller", "lefthand",
        /// "left hand", "right controller", "righthand", "right hand".
        /// Head: Camera.main, or the first Camera component found under the rig.
        /// </summary>
        private static void WireSpatialContextProvider(SpatialContextProvider spatial, GameObject xrRig)
        {
            Undo.RecordObject(spatial, "Wire SpatialContextProvider");

            // ── Head ──────────────────────────────────────────────────────────
            if (Camera.main != null)
            {
                spatial.headTransform = Camera.main.transform;
            }
            else if (xrRig != null)
            {
                Camera cam = xrRig.GetComponentInChildren<Camera>(true);
                if (cam != null) spatial.headTransform = cam.transform;
            }

            if (spatial.headTransform == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] Head camera not found. Assign SpatialContextProvider.headTransform manually.");

            if (xrRig == null)
            {
                Debug.LogWarning("[SpeechIntentSceneSetup] XR rig ('Me') not found — cannot auto-wire hand sources.");
                return;
            }

            // ── Controllers ───────────────────────────────────────────────────
            static bool IsLeft(string name)  => name.IndexOf("left",  System.StringComparison.OrdinalIgnoreCase) >= 0;
            static bool IsRight(string name) => name.IndexOf("right", System.StringComparison.OrdinalIgnoreCase) >= 0;

            static bool IsController(string name) =>
                name.IndexOf("controller", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("hand",       System.StringComparison.OrdinalIgnoreCase) >= 0;

            Transform leftTransform  = null;
            Transform rightTransform = null;

            foreach (Transform t in xrRig.GetComponentsInChildren<Transform>(true))
            {
                if (!IsController(t.name)) continue;
                if (IsLeft(t.name)  && leftTransform  == null) leftTransform  = t;
                if (IsRight(t.name) && rightTransform == null) rightTransform = t;
            }

            if (leftTransform == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] Left controller transform not found under 'Me'. Assign SpatialContextProvider.leftHandSource manually.");
            else
                spatial.leftHandSource = GetOrAdd<PointingSource>(leftTransform.gameObject);

            if (rightTransform == null)
                Debug.LogWarning("[SpeechIntentSceneSetup] Right controller transform not found under 'Me'. Assign SpatialContextProvider.rightHandSource manually.");
            else
                spatial.rightHandSource = GetOrAdd<PointingSource>(rightTransform.gameObject);
        }

        private static void WireUiPanel(UiPanelController controller, string key, string goPath)
        {
            // Idempotent: skip if a panel with this key is already registered.
            foreach (UiPanelController.PanelEntry e in controller.panels)
                if (string.Equals(e?.key, key, System.StringComparison.OrdinalIgnoreCase)) return;

            GameObject go = GameObject.Find(goPath);
            if (go == null)
            {
                Debug.LogWarning($"[SpeechIntentSceneSetup] UI panel '{goPath}' not found. Add '{key}' to uiPanels.panels manually.");
                return;
            }

            controller.panels.Add(new UiPanelController.PanelEntry { key = key, root = go });
        }
    }
}
