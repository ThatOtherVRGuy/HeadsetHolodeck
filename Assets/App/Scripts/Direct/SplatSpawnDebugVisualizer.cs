// SPDX-License-Identifier: MIT

using UnityEngine;

namespace WorldLabs.Runtime.Tools
{
    [ExecuteAlways]
    public sealed class SplatSpawnDebugVisualizer : MonoBehaviour
    {
        [Header("Source")]
        public Transform localSpace;

        [SerializeField] SplatSpawnMetadata metadata;
        [SerializeField] SplatSpawnDebugData debugData;
        [SerializeField] Vector3 placementOffset;

        [Header("Visibility")]
        public bool drawAlways = true;
        public bool buildRuntimeLineRenderers = true;
        public bool drawNormalLines = true;
        public bool drawLongAxisLines = true;
        public bool drawConsensus = true;
        public bool drawSpawnPose = true;
        public bool drawBounds = true;

        [Header("Sizing")]
        [Min(0.01f)] public float normalLineLength = 1.5f;
        [Min(0.001f)] public float lineWidth = 0.01f;
        [Min(0.01f)] public float markerRadius = 0.12f;
        [Min(0.01f)] public float spawnArrowLength = 0.75f;

        [Header("Colors")]
        public Color normalLineColor = new Color(0.2f, 0.85f, 1f, 0.8f);
        public Color longAxisLineColor = new Color(1f, 0.15f, 0.9f, 0.8f);
        public Color consensusColor = new Color(1f, 0.85f, 0.1f, 1f);
        public Color longAxisConsensusColor = new Color(1f, 0.1f, 0.75f, 1f);
        public Color spawnColor = new Color(0.1f, 1f, 0.35f, 1f);
        public Color lookAtColor = new Color(1f, 0.25f, 0.85f, 1f);
        public Color boundsColor = new Color(1f, 1f, 1f, 0.65f);

        Transform _lineRoot;
        Material _lineMaterial;

        public void SetDebugData(SplatSpawnMetadata spawnMetadata, SplatSpawnDebugData data, Vector3 offset)
        {
            metadata = spawnMetadata;
            debugData = data;
            placementOffset = offset;
            RebuildRuntimeLines();
        }

        public void Clear()
        {
            metadata = null;
            debugData = null;
            placementOffset = Vector3.zero;
            ClearRuntimeLines();
        }

        void Reset()
        {
            localSpace = transform;
        }

        void OnDrawGizmos()
        {
            if (drawAlways)
                Draw();
        }

        void OnDrawGizmosSelected()
        {
            Draw();
        }

        void Draw()
        {
            if (!enabled || metadata == null)
                return;

            Transform space = localSpace != null ? localSpace : transform;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;

            Gizmos.matrix = space.localToWorldMatrix;

            if (drawBounds)
            {
                Bounds placedBounds = metadata.bounds;
                placedBounds.center += placementOffset;
                Gizmos.color = boundsColor;
                Gizmos.DrawWireCube(placedBounds.center, placedBounds.size);
            }

            if (debugData != null && drawNormalLines)
            {
                Gizmos.color = normalLineColor;
                for (int i = 0; i < debugData.normalLines.Count; i++)
                {
                    SplatSpawnNormalLine line = debugData.normalLines[i];
                    Vector3 p = line.point + placementOffset;
                    Vector3 d = line.direction.normalized * normalLineLength * 0.5f;
                    Gizmos.DrawLine(p - d, p + d);
                }
            }

            if (debugData != null && drawLongAxisLines)
            {
                Gizmos.color = longAxisLineColor;
                for (int i = 0; i < debugData.longAxisNormalLines.Count; i++)
                {
                    SplatSpawnNormalLine line = debugData.longAxisNormalLines[i];
                    Vector3 p = line.point + placementOffset;
                    Vector3 d = line.direction.normalized * normalLineLength * 0.5f;
                    Gizmos.DrawLine(p - d, p + d);
                }
            }

            if (debugData != null && drawConsensus && debugData.hasConsensusPoint)
            {
                Gizmos.color = consensusColor;
                Gizmos.DrawSphere(debugData.consensusPoint + placementOffset, markerRadius);
            }

            if (debugData != null && drawConsensus && debugData.hasLongAxisConsensusPoint)
            {
                Gizmos.color = longAxisConsensusColor;
                Gizmos.DrawSphere(debugData.longAxisConsensusPoint + placementOffset, markerRadius);
            }

            if (metadata.hasPose && drawSpawnPose)
            {
                Gizmos.color = spawnColor;
                Gizmos.DrawSphere(metadata.spawn, markerRadius);
                Gizmos.DrawLine(metadata.spawn, metadata.spawn + metadata.rotation * Vector3.forward * spawnArrowLength);

                Gizmos.color = lookAtColor;
                Gizmos.DrawSphere(metadata.lookAt, markerRadius * 0.75f);
                Gizmos.DrawLine(metadata.spawn, metadata.lookAt);
            }

            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }

        void RebuildRuntimeLines()
        {
            ClearRuntimeLines();
            if (!buildRuntimeLineRenderers || debugData == null)
                return;

            _lineRoot = new GameObject("Runtime Normal Lines").transform;
            _lineRoot.SetParent(transform, false);

            if (_lineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                _lineMaterial = new Material(shader)
                {
                    name = "Splat Spawn Debug Line Material"
                };
            }

            Transform space = localSpace != null ? localSpace : transform;
            Matrix4x4 localToWorld = space.localToWorldMatrix;

            if (drawNormalLines)
            {
                for (int i = 0; i < debugData.normalLines.Count; i++)
                {
                    SplatSpawnNormalLine line = debugData.normalLines[i];
                    Vector3 p = line.point + placementOffset;
                    Vector3 d = line.direction.normalized * normalLineLength * 0.5f;
                    AddLine("NormalLine_" + i, localToWorld.MultiplyPoint3x4(p - d), localToWorld.MultiplyPoint3x4(p + d), normalLineColor);
                }
            }

            if (drawLongAxisLines)
            {
                for (int i = 0; i < debugData.longAxisNormalLines.Count; i++)
                {
                    SplatSpawnNormalLine line = debugData.longAxisNormalLines[i];
                    Vector3 p = line.point + placementOffset;
                    Vector3 d = line.direction.normalized * normalLineLength * 0.5f;
                    AddLine("LongAxisLine_" + i, localToWorld.MultiplyPoint3x4(p - d), localToWorld.MultiplyPoint3x4(p + d), longAxisLineColor);
                }
            }

            if (metadata != null && metadata.hasPose && drawSpawnPose)
            {
                AddLine("SpawnToLookAt", localToWorld.MultiplyPoint3x4(metadata.spawn), localToWorld.MultiplyPoint3x4(metadata.lookAt), lookAtColor);
                AddLine(
                    "SpawnForward",
                    localToWorld.MultiplyPoint3x4(metadata.spawn),
                    localToWorld.MultiplyPoint3x4(metadata.spawn + metadata.rotation * Vector3.forward * spawnArrowLength),
                    spawnColor);
            }
        }

        void AddLine(string lineName, Vector3 start, Vector3 end, Color color)
        {
            GameObject go = new GameObject(lineName);
            go.transform.SetParent(_lineRoot, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.startColor = color;
            line.endColor = color;
            line.material = _lineMaterial;
        }

        void ClearRuntimeLines()
        {
            if (_lineRoot == null)
                return;

            if (Application.isPlaying)
                Destroy(_lineRoot.gameObject);
            else
                DestroyImmediate(_lineRoot.gameObject);
            _lineRoot = null;
        }
    }
}
