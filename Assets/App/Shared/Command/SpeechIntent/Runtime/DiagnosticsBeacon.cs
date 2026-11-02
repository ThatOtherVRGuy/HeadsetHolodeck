using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace SpeechIntent
{
    /// <summary>
    /// Emits one self-contained <c>[Diag]</c> line at a fixed interval so a captured log can be read
    /// without attaching to the device.
    ///
    /// Friday's hardware session may not have an agent attached; the owner captures logs and sends them.
    /// Every line therefore names the values that decide a pass/fail â€” app state, input availability, hands,
    /// controllers, view mode, current world â€” rather than requiring a second look at the same moment.
    ///
    /// Installs itself after scene load, like the debug command bridge, so nothing needs wiring.
    /// </summary>
    public class DiagnosticsBeacon : MonoBehaviour
    {
        [Tooltip("Seconds between diagnostic lines. 5 s is dense enough to follow a test, sparse enough " +
                 "to leave the log readable.")]
        public float intervalSeconds = 5f;

        [Tooltip("Also log a line whenever a controller device is added or removed.")]
        public bool logDeviceChanges = true;

        float _nextEmit;
        int _lastFrameCount;
        float _lastFpsTime;
        float _fps;

        static DiagnosticsBeacon _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (_instance != null) return;

            var existing = FindFirstObjectByType<DiagnosticsBeacon>(FindObjectsInactive.Include);
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var go = new GameObject("DiagnosticsBeacon");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DiagnosticsBeacon>();
        }

        void OnEnable()
        {
            _instance = this;
            if (logDeviceChanges)
                InputSystem.onDeviceChange += OnDeviceChange;

            Debug.Log($"[Diag] beacon online. device='{SystemInfo.deviceModel}' os='{SystemInfo.operatingSystem}' " +
                      $"unity='{Application.unityVersion}' app='{Application.version}' interval={intervalSeconds:0.#}s");
        }

        void OnDisable()
        {
            if (logDeviceChanges)
                InputSystem.onDeviceChange -= OnDeviceChange;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            // A controller appearing or disappearing in the middle of a test is the single most confusing
            // thing to reconstruct afterwards.
            string name = device != null ? $"{device.displayName} ({device.layout})" : "(null)";
            Debug.Log($"[Diag] input device {change}: {name}");
        }

        void Update()
        {
            // FPS is smoothed over the sampling window rather than per frame, so one hitch does not define it.
            _lastFrameCount++;
            float now = Time.realtimeSinceStartup;
            if (now - _lastFpsTime >= 1f)
            {
                _fps = _lastFrameCount / (now - _lastFpsTime);
                _lastFrameCount = 0;
                _lastFpsTime = now;
            }

            if (now < _nextEmit) return;
            _nextEmit = now + Mathf.Max(1f, intervalSeconds);

            Emit();
        }

        void Emit()
        {
            try
            {
                Debug.Log($"[Diag] {BuildLine()}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Diag] line failed: {ex.Message}");
            }
        }

        string BuildLine()
        {
            return string.Join(" | ", new[]
            {
                $"fps={_fps:0}",
                $"state={StateName()}",
                $"view={ViewSummary()}",
                $"world={WorldSummary()}",
                $"hands={HandsSummary()}",
                $"xrnodes={XrNodeSummary()}",
                $"controllers={ControllerSummary()}",
                $"pointer={PointerSummary()}",
                $"mic={MicSummary()}",
                $"stt={SttSummary()}",
                $"camera={CameraSummary()}"
            });
        }

        static string _sttCache;

        /// <summary>
        /// Whether the OS offers an Android speech recognition service, asked once.
        ///
        /// PICO OS ships no speech package on the emulator image (no package matches speech/voice/asr and no
        /// RecognitionService is registered), so this reports the hardware answer directly instead of
        /// assuming the emulator is representative.
        /// </summary>
        static string SttSummary()
        {
            if (_sttCache != null) return _sttCache;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = activityClass.GetStatic<AndroidJavaObject>("currentActivity");
                using var recognizer = new AndroidJavaClass("android.speech.SpeechRecognizer");
                bool available = recognizer.CallStatic<bool>("isRecognitionAvailable", activity);
                _sttCache = available ? "android:yes" : "android:no";
            }
            catch (Exception ex)
            {
                _sttCache = "android:error(" + ex.GetType().Name + ")";
            }
#else
            _sttCache = "editor";
#endif
            return _sttCache;
        }

        static string StateName()
        {
            var machine = FindFirstObjectByType<Holodeck.State.HolodeckStateMachine>(FindObjectsInactive.Include);
            return machine != null ? machine.CurrentState.ToString() : "none";
        }

        string _pointerCache = "none";
        float _pointerCacheAt = -999f;

        /// <summary>
        /// Which pointer is live, and what XRI thinks the input modality is.
        ///
        /// "The controllers track but nothing points" has one overwhelmingly common cause: XRI's modality
        /// manager decided the user is using hands, so the controller interactors are disabled while the
        /// joysticks (bound separately) keep working. Reflection keeps this independent of the XRI version.
        ///
        /// Scanned globally rather than from this object's scene, because the beacon lives in
        /// DontDestroyOnLoad while the rig lives in the gameplay scene. Cached because it changes rarely.
        /// </summary>
        string PointerSummary()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _pointerCacheAt < 15f) return _pointerCache;
            _pointerCacheAt = now;

            var parts = new List<string>();
            parts.Add($"uiToolkit={UiToolkitSupportFlag()}");

            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                string type = mb.GetType().Name;

                if (type == "XRInputModalityManager")
                {
                    // The manager has no public mode property, but it drives modality by toggling these four
                    // GameObjects. Their active state is therefore the modality, and the reason a whole set of
                    // interactors disappears.
                    parts.Add("modality[" +
                              $"LCtrl={ActiveOf(mb, "leftController")}," +
                              $"RCtrl={ActiveOf(mb, "rightController")}," +
                              $"LHand={ActiveOf(mb, "leftHand")}," +
                              $"RHand={ActiveOf(mb, "rightHand")}]");
                    continue;
                }

                if (type.Contains("NearFarInteractor") || type.Contains("XRRayInteractor") ||
                    type.Contains("XRGazeInteractor") || type.Contains("XRPokeInteractor"))
                {
                    string state = mb.enabled && mb.gameObject.activeInHierarchy ? "on" : "off";

                    // The parent names distinguish the hand set from the controller set, which is exactly the
                    // distinction that matters when one set is off.
                    Transform parent = mb.transform.parent;
                    string owner = parent != null ? ShortName(parent.name) : "?";
                    parts.Add($"{owner}/{ShortName(mb.gameObject.name)}={state}");
                }
            }

            _pointerCache = parts.Count == 0 ? "none" : string.Join(",", parts);
            return _pointerCache;
        }

        /// <summary>
        /// Reports XRI's global UI-interaction flag. When this is false, every interactor's own
        /// enableUIInteraction is ignored, so rays track and render but nothing can be hovered or clicked —
        /// a failure that produces no warning anywhere else.
        /// </summary>
        static string UiToolkitSupportFlag()
        {
            System.Type handler = null;
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                handler = assembly.GetType("UnityEngine.XR.Interaction.Toolkit.UI.XRUIToolkitHandler");
                if (handler != null) break;
            }

            if (handler == null) return "?";

            var property = handler.GetProperty("uiToolkitSupportEnabled",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (property == null) return "?";

            object value = property.GetValue(null);
            return value is bool enabled ? (enabled ? "true" : "false") : "?";
        }

        static string ActiveOf(object manager, string property)
        {
            // These are public *properties* on XRInputModalityManager, not fields. Reading them as fields
            // returned null and reported "unset", which wrongly implicated the scene wiring.
            var prop = manager.GetType().GetProperty(property,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var go = prop != null ? prop.GetValue(manager) as GameObject : null;
            if (go == null) return "null";
            return go.activeInHierarchy ? "on" : "off";
        }

        static string ReflectMode(object manager, string property)
        {
            var prop = manager.GetType().GetProperty(property,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            object value = prop != null ? prop.GetValue(manager) : null;
            return value != null ? value.ToString() : "?";
        }

        static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Replace(" Interactor", "").Replace(" ", "");
        }

        static string ViewSummary()
        {
            var view = FindFirstObjectByType<ViewModeController>(FindObjectsInactive.Include);
            if (view == null) return "none";

            // IsPanoAvailable / IsSplatAvailable are the guards the switch commands check, so they answer
            // "why did show panorama do nothing" directly.
            return $"splat={(view.IsSplatAvailable ? "ready" : "no")}" +
                   $",pano={(view.IsPanoAvailable ? "ready" : "no")}";
        }

        static string WorldSummary()
        {
            var autoSave = FindFirstObjectByType<Holodeck.Save.WorldConfigAutoSave>(FindObjectsInactive.Include);
            var config = autoSave != null ? autoSave.ActiveConfig : null;
            if (config == null) return "none";

            int objects = config.objects != null ? config.objects.Count : 0;
            return $"'{Trim(config.display_name, 28)}',type={config.world_source?.type ?? "?"},objects={objects}";
        }

        static string HandsSummary()
        {
            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            XRHandSubsystem hands = subsystems.FirstOrDefault(s => s != null && s.running);
            if (hands == null) return "no-subsystem";

            XRHand left = hands.leftHand;
            XRHand right = hands.rightHand;

            return $"L={(left.isTracked ? "tracked" : "lost")}," +
                   $"R={(right.isTracked ? "tracked" : "lost")}," +
                   $"aimL={HasAim(hands.leftHandCommonGestures, left)}," +
                   $"aimR={HasAim(hands.rightHandCommonGestures, right)}";
        }

        static string HasAim(XRCommonHandGestures gestures, XRHand hand)
        {
            if (!hand.isTracked) return "n/a";
            if (gestures != null && gestures.TryGetAimPose(out _)) return "native";

            // Matches XRHandsGestureSource's fallback: an aim pose can be derived from the index knuckle
            // and fingertip, so "derived" still means pointing works.
            if (hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose knuckle) &&
                hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tip) &&
                (tip.position - knuckle.position).sqrMagnitude > 0.0001f)
                return "derived";

            return "none";
        }

        /// <summary>
        /// Tracking state per XR node. A controller device existing is not the same as it being tracked, and
        /// "the ray does nothing" has completely different causes in each case.
        /// </summary>
        static string XrNodeSummary()
        {
            var nodes = new[] { UnityEngine.XR.XRNode.Head, UnityEngine.XR.XRNode.LeftHand, UnityEngine.XR.XRNode.RightHand,
                                UnityEngine.XR.XRNode.CenterEye };
            var parts = new List<string>();

            foreach (UnityEngine.XR.XRNode node in nodes)
            {
                UnityEngine.XR.InputDevice device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
                if (!device.isValid)
                {
                    parts.Add($"{node}=none");
                    continue;
                }

                bool tracked = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool value) && value;
                parts.Add($"{node}={(tracked ? "tracked" : "untracked")}");
            }

            return string.Join(",", parts);
        }

        static string ControllerSummary()
        {
            var controllers = InputSystem.devices.OfType<UnityEngine.InputSystem.XR.XRController>().ToList();
            if (controllers.Count == 0) return "none";

            var parts = new List<string>();
            foreach (UnityEngine.InputSystem.XR.XRController controller in controllers.Take(4))
                parts.Add($"{controller.name}:{controller.deviceId}");

            return $"{controllers.Count}[{string.Join(",", parts)}]";
        }

        static string MicSummary()
        {
            var ptt = FindFirstObjectByType<PushToTalkTrigger>(FindObjectsInactive.Include);
            var wake = FindFirstObjectByType<VoiceActivation.VadAsrWakeTrigger>(FindObjectsInactive.Include);

            string pttState = ptt != null && ptt.isActiveAndEnabled ? "armed" : "off";
            string wakeState = wake != null && wake.isActiveAndEnabled ? "armed" : "off";
            return $"ptt={pttState},wake={wakeState},micDevices={Microphone.devices.Length}";
        }

        static string CameraSummary()
        {
            Camera main = Camera.main;
            if (main == null)
            {
                Camera[] all = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                main = all.FirstOrDefault(c => c != null && c.enabled);
                if (main == null) return "none";
            }

            Vector3 p = main.transform.position;
            return $"cam=({p.x:0.#},{p.y:0.#},{p.z:0.#})";
        }

        static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max) + "â€¦";
        }
    }
}
