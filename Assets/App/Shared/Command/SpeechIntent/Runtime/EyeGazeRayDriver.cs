using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace SpeechIntent
{
    /// <summary>
    /// Aims the gaze interactor from the eye tracking device, which nothing else in the rig was doing.
    ///
    /// <c>XRGazeInteractor</c> derives from <c>XRRayInteractor</c> and never reads an eye pose: it fires from
    /// <c>rayOriginTransform</c> (or its own transform). In this rig that transform was "Gaze Stabilized",
    /// whose <c>XRTransformStabilizer</c> targets the interactor itself — a loop with nothing to stabilise —
    /// and its parent is "Camera Offset", the rig root, so the ray sat at a fixed pose. The app therefore
    /// reported <c>Gaze=on</c> (the interactor is enabled) while never hovering anything, which is exactly
    /// what the emulator test found.
    ///
    /// This drives the interactor from <c>XRNode.CenterEye</c> each frame, and falls back to the main camera
    /// when no eye pose is available, so gaze degrades to head gaze instead of freezing.
    ///
    /// Installs itself after scene load; the interactor type is resolved by name so this file does not take
    /// a hard dependency on the XRI assembly.
    /// </summary>
    public class EyeGazeRayDriver : MonoBehaviour
    {
        const string GazeInteractorTypeName = "UnityEngine.XR.Interaction.Toolkit.Interactors.XRGazeInteractor";

        [Tooltip("Log whenever the pose source changes (eye / camera / none).")]
        public bool logSourceChanges = true;

        Component _gazeInteractor;
        Transform _gazeTransform;
        PropertyInfo _rayOriginProperty;
        Camera _fallbackCamera;
        string _lastSource;

        static EyeGazeRayDriver _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (_instance != null) return;
            var existing = FindFirstObjectByType<EyeGazeRayDriver>(FindObjectsInactive.Include);
            if (existing != null) { _instance = existing; return; }

            var host = new GameObject("EyeGazeRayDriver");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<EyeGazeRayDriver>();
        }

        void OnEnable()
        {
            _instance = this;
            ResolveInteractor();
        }

        void ResolveInteractor()
        {
            Type interactorType = FindType(GazeInteractorTypeName);
            if (interactorType == null)
            {
                Debug.LogWarning($"[EyeGaze] {GazeInteractorTypeName} not found; gaze ray will not be driven.");
                return;
            }

            foreach (var candidate in FindObjectsByType(interactorType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var component = candidate as Component;
                if (component == null) continue;
                _gazeInteractor = component;
                break;
            }

            if (_gazeInteractor == null)
            {
                Debug.LogWarning("[EyeGaze] no gaze interactor in the scene; gaze ray will not be driven.");
                return;
            }

            _rayOriginProperty = interactorType.GetProperty("rayOriginTransform",
                BindingFlags.Public | BindingFlags.Instance);

            // Point the ray at the interactor itself. The previously assigned origin ("Gaze Stabilized")
            // carried a stabiliser aimed back at the interactor, so leaving it in place would keep the ray
            // pinned to that object's frozen pose.
            if (_rayOriginProperty != null && _rayOriginProperty.CanWrite)
            {
                var previous = _rayOriginProperty.GetValue(_gazeInteractor) as Transform;
                if (previous != null)
                {
                    var stabilizer = previous.GetComponent("XRTransformStabilizer") as Behaviour;
                    if (stabilizer != null) stabilizer.enabled = false;
                    Debug.Log($"[EyeGaze] detached ray origin '{previous.name}' and disabled its stabiliser.");
                }
                _rayOriginProperty.SetValue(_gazeInteractor, null);
            }

            _gazeTransform = _gazeInteractor.transform;
            Debug.Log($"[EyeGaze] driving gaze interactor on '{_gazeInteractor.gameObject.name}' from the eye device.");
        }

        void LateUpdate()
        {
            if (_gazeInteractor == null) { ResolveInteractor(); if (_gazeInteractor == null) return; }

            bool eyeValid = TryGetEyePose(out Pose eyePose);
            Pose pose;
            string source;

            if (eyeValid) { pose = eyePose; source = "eye"; }
            else if (TryGetCameraPose(out Pose cameraPose)) { pose = cameraPose; source = "camera"; }
            else { pose = default; source = "none"; }

            if (source == "none") return;

            _gazeTransform.SetPositionAndRotation(pose.position, pose.rotation);

            if (logSourceChanges && source != _lastSource)
            {
                _lastSource = source;
                if (source == "eye")
                    Debug.Log($"[EyeGaze] using {( _usingGazeUsage ? "gazePosition/gazeRotation" : "centre-eye fallback")} " +
                              $"pos=({pose.position.x:0.00},{pose.position.y:0.00},{pose.position.z:0.00}) " +
                              $"euler=({pose.rotation.eulerAngles.x:0},{pose.rotation.eulerAngles.y:0},{pose.rotation.eulerAngles.z:0}).");
                else
                    Debug.LogWarning("[EyeGaze] no eye pose; falling back to head gaze from the main camera.");
            }
        }

        /// <summary>
        /// Reads the gaze aim pose.
        ///
        /// OpenXR's eye-gaze feature publishes <c>EyeTrackingUsages.gazePosition</c> and
        /// <c>gazeRotation</c> on a device carrying the <see cref="InputDeviceCharacteristics.EyeTracking"/>
        /// characteristic — that is the gaze ray. <c>CommonUsages.centerEyePosition/Rotation</c> are the eye
        /// *centre*, i.e. essentially the head pose, and reading those from whatever device sits at
        /// <c>XRNode.CenterEye</c> produced a ray that followed the head instead of the gaze: it reported a
        /// valid pose and still never hovered anything, which is exactly what the emulator run showed.
        ///
        /// The usages are resolved by reflection so this file needs no compile-time dependency on the
        /// OpenXR package, and the centre-eye path stays as a fallback for runtimes that only offer that.
        /// </summary>
        static bool TryGetEyePose(out Pose pose)
        {
            pose = default;
            ResolveEyeTrackingUsages();

            var eyeDevices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, eyeDevices);

            // Prefer a device that actually reports a gaze pose, rather than assuming the first is right.
            foreach (InputDevice device in eyeDevices)
            {
                if (!device.isValid) continue;
                if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) continue;

                if (_gazePositionUsage.HasValue && _gazeRotationUsage.HasValue &&
                    device.TryGetFeatureValue(_gazePositionUsage.Value, out Vector3 gazePosition) &&
                    device.TryGetFeatureValue(_gazeRotationUsage.Value, out Quaternion gazeRotation))
                {
                    pose = new Pose(gazePosition, gazeRotation);
                    _usingGazeUsage = true;
                    return true;
                }
            }

            // Centre-eye fallback: head-relative eye centre, not a gaze direction, but better than nothing.
            InputDevice centreEye = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (centreEye.isValid)
            {
                if (centreEye.TryGetFeatureValue(CommonUsages.isTracked, out bool centreTracked) && !centreTracked)
                    return false;

                if (centreEye.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 position) &&
                    centreEye.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation))
                {
                    pose = new Pose(position, rotation);
                    _usingGazeUsage = false;
                    return true;
                }
            }

            return false;
        }

        static InputFeatureUsage<Vector3>? _gazePositionUsage;
        static InputFeatureUsage<Quaternion>? _gazeRotationUsage;
        static bool _usagesResolved;
        static bool _usingGazeUsage;

        static void ResolveEyeTrackingUsages()
        {
            if (_usagesResolved) return;
            _usagesResolved = true;

            Type usages = FindType("UnityEngine.XR.OpenXR.Features.Interactions.EyeTrackingUsages");
            if (usages == null)
            {
                Debug.LogWarning("[EyeGaze] OpenXR EyeTrackingUsages not found; falling back to centre-eye.");
                return;
            }

            object positionField = usages.GetField("gazePosition", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object rotationField = usages.GetField("gazeRotation", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

            if (positionField is InputFeatureUsage<Vector3> positionUsage) _gazePositionUsage = positionUsage;
            if (rotationField is InputFeatureUsage<Quaternion> rotationUsage) _gazeRotationUsage = rotationUsage;
        }

        static bool TryGetCameraPose(out Pose pose)
        {
            pose = default;
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var candidate in cameras)
                {
                    if (candidate != null && candidate.enabled) { camera = candidate; break; }
                }
            }

            if (camera == null) return false;
            pose = new Pose(camera.transform.position, camera.transform.rotation);
            return true;
        }

        static Type FindType(string fullName)
        {
            Type direct = Type.GetType(fullName);
            if (direct != null) return direct;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName);
                if (type != null) return type;
            }

            return null;
        }
    }
}
