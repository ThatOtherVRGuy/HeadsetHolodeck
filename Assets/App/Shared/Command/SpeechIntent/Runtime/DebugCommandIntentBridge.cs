using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace SpeechIntent
{
    /// <summary>
    /// Drives the app with an Android broadcast so a test (or another tool) can issue commands without
    /// a microphone or a pointer:
    ///
    ///   adb shell am broadcast -a com.QLC.HeadsetHolodeck.COMMAND --es command "load rain room"
    ///
    /// Commands go through <see cref="VoiceCommandRouter.SubmitTypedCommand"/>, the same path the LCARS
    /// keyboard uses, so the real parser, router, dispatcher and restore logic are exercised — not a
    /// shortcut that could pass while the spoken route fails.
    ///
    /// This is a test affordance. It is reachable by any app on the device, so leave
    /// <see cref="enableInReleaseBuilds"/> false for anything shipped to users.
    /// </summary>
    public class DebugCommandIntentBridge : MonoBehaviour
    {
        public const string CommandAction = "com.QLC.HeadsetHolodeck.COMMAND";

        [Header("Dependencies")]
        public VoiceCommandRouter voiceCommandRouter;

        [Header("Settings")]
        [Tooltip("Allow the broadcast bridge in a release build. Handy while testing on a headset, " +
                 "but any app on the device could then send commands, so disable before shipping.")]
        public bool enableInReleaseBuilds = true;

        [Tooltip("Mirror each received command to the Ops panel so it is visible in headset.")]
        public bool reportCommands = true;

        [Tooltip("Also accept commands dropped into debug_command.txt in the app's files directory. " +
                 "Works on every Android version without broadcast permissions.")]
        public bool enableFileChannel = true;

        public const string FileCommandName = "debug_command.txt";

        readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        float _nextFilePoll;

        AndroidJavaObject _activity;
        BroadcastProxy _proxy;
        bool _registered;

        /// <summary>
        /// Installs the bridge on the router's object once a scene is up, so the scene and prefabs need
        /// no wiring for a test affordance that may be removed later.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            var router = FindFirstObjectByType<VoiceCommandRouter>(FindObjectsInactive.Include);
            if (router == null)
                return;

            var bridge = router.GetComponent<DebugCommandIntentBridge>();
            if (bridge == null)
                bridge = router.gameObject.AddComponent<DebugCommandIntentBridge>();

            bridge.voiceCommandRouter = router;
        }

        void OnEnable()
        {
            if (!Debug.isDebugBuild && !enableInReleaseBuilds)
            {
                Debug.Log("[DebugCommandIntentBridge] Disabled in release build (enableInReleaseBuilds is off).", this);
                return;
            }

            TryRegister();
        }

        void OnDisable()
        {
            TryUnregister();
        }

        void Update()
        {
            if (enableFileChannel)
                PollCommandFile();

            while (_pending.TryDequeue(out string command))
                Dispatch(command);
        }

        /// <summary>
        /// Reads and removes a single command file. This exists because a runtime BroadcastReceiver
        /// needs the API 33 export flag, and calling that overload through AndroidJavaObject fails its
        /// JNI signature check; a file drop needs no Android APIs and no permissions.
        /// </summary>
        void PollCommandFile()
        {
            if (Time.unscaledTime < _nextFilePoll)
                return;

            _nextFilePoll = Time.unscaledTime + 0.5f;

            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, FileCommandName);
                if (!System.IO.File.Exists(path))
                    return;

                string command = System.IO.File.ReadAllText(path).Trim();
                System.IO.File.Delete(path);
                if (!string.IsNullOrWhiteSpace(command))
                    _pending.Enqueue(command);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DebugCommandIntentBridge] Command file read failed: {ex.Message}", this);
            }
        }

        void Dispatch(string command)
        {
            Debug.Log($"[DebugCommandIntentBridge] Received command: '{command}'", this);
            if (reportCommands)
                ArchStatusBus.Info($"Intent command: {command}", "DEBUG");

            // Replay recorded speech through the on-device recogniser. This exists so the speech path can be
            // exercised without a working microphone: send "replay" to run every clip in the fixture folder,
            // or "replay <file-or-folder>". It runs the real AsrService, so a result here means the same code
            // path the microphone feeds.
            string trimmed = command.Trim();
            if (trimmed.Equals("replay", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("replay ", StringComparison.OrdinalIgnoreCase))
            {
                string spec = trimmed.Length > 6 ? trimmed.Substring(6).Trim() : string.Empty;
                StartCoroutine(VoiceFixtureReplay.Run(spec, line => Debug.Log(line, this)));
                return;
            }

            if (voiceCommandRouter == null)
            {
                Debug.LogWarning("[DebugCommandIntentBridge] voiceCommandRouter is not assigned; command dropped.", this);
                return;
            }

            voiceCommandRouter.SubmitTypedCommand(command);
        }

        void TryRegister()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                _activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (_activity == null)
                {
                    Debug.LogWarning("[DebugCommandIntentBridge] No current activity; bridge not registered.", this);
                    return;
                }

                using var filter = new AndroidJavaObject("android.content.IntentFilter", CommandAction);
                _proxy = new BroadcastProxy(command =>
                {
                    if (!string.IsNullOrWhiteSpace(command)) _pending.Enqueue(command);
                });

                using var buildVersion = new AndroidJavaClass("android.os.Build$VERSION");
                int sdkInt = buildVersion.GetStatic<int>("SDK_INT");

                // Android 13 (API 33) requires an explicit export flag for a runtime receiver that
                // accepts broadcasts from other apps; shell `am broadcast` counts as another app.
                const int ReceiverExported = 2;
                if (sdkInt >= 33)
                    _activity.Call("registerReceiver", _proxy, filter, ReceiverExported);
                else
                    _activity.Call("registerReceiver", _proxy, filter);

                _registered = true;
                Debug.Log($"[DebugCommandIntentBridge] Listening for '{CommandAction}' (SDK {sdkInt}).", this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DebugCommandIntentBridge] Registration failed: {ex.Message}", this);
                TryUnregister();
            }
#endif
        }

        void TryUnregister()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_registered || _activity == null || _proxy == null)
                return;

            try
            {
                _activity.Call("unregisterReceiver", _proxy);
                Debug.Log("[DebugCommandIntentBridge] Unregistered.", this);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DebugCommandIntentBridge] Unregister failed: {ex.Message}", this);
            }
            finally
            {
                _registered = false;
                _proxy = null;
                _activity?.Dispose();
                _activity = null;
            }
#endif
        }

        /// <summary>Receives on the Android UI thread and hands the text to the main thread via <see cref="_pending"/>.</summary>
        class BroadcastProxy : AndroidJavaProxy
        {
            readonly Action<string> _onCommand;

            public BroadcastProxy(Action<string> onCommand)
                : base("android.content.BroadcastReceiver")
            {
                _onCommand = onCommand;
            }

            // ReSharper disable once InconsistentNaming — name must match the Android method.
            void onReceive(AndroidJavaObject context, AndroidJavaObject intent)
            {
                try
                {
                    string command = intent?.Call<string>("getStringExtra", "command");
                    _onCommand(command);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DebugCommandIntentBridge] onReceive failed: {ex.Message}");
                }
            }
        }
    }
}
