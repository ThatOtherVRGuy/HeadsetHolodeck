using System;
using System.Collections.Generic;
using SpeechIntent;
using UnityEngine;

namespace SpeechIntent.Behaviors
{
    public class RuntimeBehaviorHost : MonoBehaviour
    {
        readonly Dictionary<string, RuntimeBehavior> _behaviors = new Dictionary<string, RuntimeBehavior>(StringComparer.OrdinalIgnoreCase);

        public int BehaviorCount => _behaviors.Count;

        void Update()
        {
            Tick(Time.deltaTime);
        }

        public bool StartSpin(string id, Vector3 axis, float speedDegreesPerSecond, Space space)
        {
            if (axis.sqrMagnitude <= 0.0001f || Mathf.Approximately(speedDegreesPerSecond, 0f))
                return false;

            AddOrReplace(new SpinBehavior(id, axis.normalized, speedDegreesPerSecond, space));
            return true;
        }

        public bool StartOrbit(string id, Transform center, float radius, float speedDegreesPerSecond)
        {
            if (center == null || Mathf.Approximately(speedDegreesPerSecond, 0f))
                return false;

            AddOrReplace(new OrbitBehavior(id, transform, center, radius, speedDegreesPerSecond));
            return true;
        }

        public bool StartHandFollow(string id, BodyAnchor anchor, SpatialSnapshot spatial, Vector3 offset, bool attachStyle)
        {
            return StartHandFollow(id, anchor, spatial, offset, attachStyle, null);
        }

        public bool StartHandFollow(
            string id,
            BodyAnchor anchor,
            SpatialSnapshot spatial,
            Vector3 offset,
            bool attachStyle,
            SpatialContextProvider spatialContextProvider)
        {
            if (anchor == BodyAnchor.None)
                return false;

            SpatialSnapshot initialSpatial = spatialContextProvider != null
                ? spatialContextProvider.CaptureSnapshot()
                : spatial;

            if (!BodyAnchorResolver.TryResolve(initialSpatial, anchor, HandSelection.None, out _, out _))
                return false;

            AddOrReplace(new HandFollowBehavior(id, anchor, initialSpatial, offset, attachStyle, spatialContextProvider));
            return true;
        }

        public bool StopBehavior(string behaviorName)
        {
            if (string.IsNullOrWhiteSpace(behaviorName))
                return false;

            bool removed = false;
            foreach (string key in FindMatchingKeys(behaviorName))
                removed |= _behaviors.Remove(key);

            return removed;
        }

        public bool StopAll()
        {
            bool hadBehaviors = _behaviors.Count > 0;
            _behaviors.Clear();
            return hadBehaviors;
        }

        public bool HasBehavior(string behaviorName)
        {
            if (string.IsNullOrWhiteSpace(behaviorName))
                return false;

            string normalized = NormalizeKey(behaviorName);
            foreach (RuntimeBehavior behavior in _behaviors.Values)
            {
                if (string.Equals(behavior.Key, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(behavior.Kind, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static int StopAllHosts()
        {
            int stoppedHosts = 0;
            RuntimeBehaviorHost[] hosts = FindObjectsByType<RuntimeBehaviorHost>(FindObjectsSortMode.None);
            foreach (RuntimeBehaviorHost host in hosts)
            {
                if (host.StopAll())
                    stoppedHosts++;
            }

            return stoppedHosts;
        }

        public void TickForTests(float deltaTime)
        {
            Tick(deltaTime);
        }

        void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || _behaviors.Count == 0)
                return;

            foreach (RuntimeBehavior behavior in _behaviors.Values)
                behavior.Tick(transform, deltaTime);
        }

        void AddOrReplace(RuntimeBehavior behavior)
        {
            _behaviors[behavior.Key] = behavior;
        }

        List<string> FindMatchingKeys(string behaviorName)
        {
            string normalized = NormalizeKey(behaviorName);
            List<string> keys = new List<string>();
            foreach (KeyValuePair<string, RuntimeBehavior> pair in _behaviors)
            {
                if (string.Equals(pair.Key, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Value.Kind, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(pair.Key);
                }
            }

            return keys;
        }

        static string NormalizeKey(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        abstract class RuntimeBehavior
        {
            protected RuntimeBehavior(string id, string kind)
            {
                Key = string.IsNullOrWhiteSpace(id) ? kind : id.Trim();
                Kind = kind;
            }

            public string Key { get; }
            public string Kind { get; }

            public abstract void Tick(Transform target, float deltaTime);
        }

        sealed class SpinBehavior : RuntimeBehavior
        {
            readonly Vector3 _axis;
            readonly float _speedDegreesPerSecond;
            readonly Space _space;

            public SpinBehavior(string id, Vector3 axis, float speedDegreesPerSecond, Space space)
                : base(id, "spin")
            {
                _axis = axis;
                _speedDegreesPerSecond = speedDegreesPerSecond;
                _space = space;
            }

            public override void Tick(Transform target, float deltaTime)
            {
                target.Rotate(_axis, _speedDegreesPerSecond * deltaTime, _space);
            }
        }

        sealed class OrbitBehavior : RuntimeBehavior
        {
            readonly Transform _center;
            readonly float _speedDegreesPerSecond;
            readonly float _verticalOffset;
            Vector3 _horizontalOffset;

            public OrbitBehavior(string id, Transform target, Transform center, float radius, float speedDegreesPerSecond)
                : base(id, "orbit")
            {
                _center = center;
                _speedDegreesPerSecond = speedDegreesPerSecond;

                Vector3 offset = target.position - center.position;
                _verticalOffset = offset.y;
                _horizontalOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
                float resolvedRadius = radius > 0f ? radius : _horizontalOffset.magnitude;

                if (resolvedRadius <= 0.0001f)
                    resolvedRadius = 1f;

                _horizontalOffset = _horizontalOffset.sqrMagnitude > 0.0001f
                    ? _horizontalOffset.normalized * resolvedRadius
                    : Vector3.forward * resolvedRadius;
            }

            public override void Tick(Transform target, float deltaTime)
            {
                if (_center == null)
                    return;

                _horizontalOffset = Quaternion.AngleAxis(_speedDegreesPerSecond * deltaTime, Vector3.up) * _horizontalOffset;
                target.position = _center.position + _horizontalOffset + Vector3.up * _verticalOffset;
            }
        }

        sealed class HandFollowBehavior : RuntimeBehavior
        {
            readonly BodyAnchor _anchor;
            readonly SpatialSnapshot _spatial;
            readonly Vector3 _offset;
            readonly bool _attachStyle;
            readonly SpatialContextProvider _spatialContextProvider;

            public HandFollowBehavior(
                string id,
                BodyAnchor anchor,
                SpatialSnapshot spatial,
                Vector3 offset,
                bool attachStyle,
                SpatialContextProvider spatialContextProvider)
                : base(id, attachStyle ? "attach_to_hand" : "follow_hand")
            {
                _anchor = anchor;
                _spatial = spatial;
                _offset = offset;
                _attachStyle = attachStyle;
                _spatialContextProvider = spatialContextProvider;
            }

            public override void Tick(Transform target, float deltaTime)
            {
                SpatialSnapshot currentSpatial = _spatialContextProvider != null
                    ? _spatialContextProvider.CaptureSnapshot()
                    : _spatial;

                if (!BodyAnchorResolver.TryResolve(currentSpatial, _anchor, HandSelection.None, out Vector3 position, out Quaternion rotation))
                    return;

                target.position = position + rotation * _offset;
                if (_attachStyle)
                    target.rotation = rotation;
            }
        }
    }
}
