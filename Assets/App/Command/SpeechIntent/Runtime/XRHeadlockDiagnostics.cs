using System.Text;
using UnityEngine;

namespace SpeechIntent
{
    public sealed class XRHeadlockDiagnostics : MonoBehaviour
    {
        [Header("References")]
        public Transform headTransform;
        public Transform xrRoot;

        [Header("World Probe")]
        public bool createWorldProbe = true;
        public Vector3 probePosition = new Vector3(0f, 1.4f, 2f);
        public float probeScale = 0.18f;

        [Header("Logging")]
        public bool logPose = true;
        public float logIntervalSeconds = 1f;

        Vector3 _initialHeadPosition;
        Quaternion _initialHeadRotation;
        float _nextLogTime;
        GameObject _probe;

        void Awake()
        {
            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;

            if (xrRoot == null && headTransform != null)
                xrRoot = FindTopParent(headTransform);

            if (headTransform != null)
            {
                _initialHeadPosition = headTransform.position;
                _initialHeadRotation = headTransform.rotation;
            }
        }

        void OnEnable()
        {
            if (createWorldProbe)
                EnsureProbe();

            LogSnapshot("enabled");
        }

        void Update()
        {
            if (!logPose || Time.unscaledTime < _nextLogTime)
                return;

            _nextLogTime = Time.unscaledTime + Mathf.Max(0.1f, logIntervalSeconds);
            LogSnapshot("tick");
        }

        [ContextMenu("Log XR Headlock Snapshot")]
        public void LogSnapshotFromMenu()
        {
            LogSnapshot("manual");
        }

        void EnsureProbe()
        {
            if (_probe != null)
                return;

            _probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _probe.name = "XRHeadlockDiagnostics_WorldProbe";
            _probe.transform.SetPositionAndRotation(probePosition, Quaternion.identity);
            _probe.transform.localScale = Vector3.one * probeScale;

            Renderer renderer = _probe.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = Color.magenta;

            DontDestroyOnLoad(_probe);
        }

        void LogSnapshot(string reason)
        {
            if (headTransform == null)
            {
                Debug.LogWarning("[XRHeadlockDiagnostics] No headTransform assigned and Camera.main was not found.", this);
                return;
            }

            Vector3 headDelta = headTransform.position - _initialHeadPosition;
            float rotationDelta = Quaternion.Angle(_initialHeadRotation, headTransform.rotation);

            var sb = new StringBuilder();
            sb.Append("[XRHeadlockDiagnostics] ").Append(reason);
            sb.Append(" head=").Append(Format(headTransform.position));
            sb.Append(" headDelta=").Append(Format(headDelta));
            sb.Append(" rotDelta=").Append(rotationDelta.ToString("0.0"));
            sb.Append(" parentChain=").Append(BuildParentChain(headTransform));

            if (xrRoot != null)
            {
                sb.Append(" xrRoot=").Append(xrRoot.name);
                sb.Append(" xrRootPos=").Append(Format(xrRoot.position));
            }

            if (_probe != null)
            {
                Vector3 probeInHeadSpace = headTransform.InverseTransformPoint(_probe.transform.position);
                sb.Append(" probeWorld=").Append(Format(_probe.transform.position));
                sb.Append(" probeHeadSpace=").Append(Format(probeInHeadSpace));
            }

            Debug.Log(sb.ToString(), this);
        }

        static Transform FindTopParent(Transform transform)
        {
            Transform current = transform;
            while (current.parent != null)
                current = current.parent;
            return current;
        }

        static string BuildParentChain(Transform transform)
        {
            var sb = new StringBuilder();
            Transform current = transform;
            while (current != null)
            {
                if (sb.Length > 0)
                    sb.Insert(0, "/");
                sb.Insert(0, current.name);
                current = current.parent;
            }
            return sb.ToString();
        }

        static string Format(Vector3 value)
        {
            return $"({value.x:0.00},{value.y:0.00},{value.z:0.00})";
        }
    }
}
