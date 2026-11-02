// SPDX-License-Identifier: MIT

using UnityEngine;

namespace WorldLabs.Runtime.Tools
{
    /// <summary>
    /// Draws debug gizmos for a SplatFloorEstimate.
    ///
    /// Attach this to the spawned splat GameObject (recommended), then call SetEstimate(...)
    /// with the result returned by SplatFloorAnalyzer / RuntimeSplatFloorLoader.
    ///
    /// The estimate is assumed to be in the local coordinate space of the target object.
    /// </summary>
    [ExecuteAlways]
    public class SplatFloorDebugGizmos : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Local-space transform used when drawing the estimate. Defaults to this transform.")]
        public Transform localSpace;

        [SerializeField]
        private SplatFloorEstimate estimate;

        [Header("Visibility")]
        public bool drawAlways = true;
        public bool drawWhenSelected = true;

        [Header("Shapes")]
        public bool drawAnalyzedBounds = true;
        public bool drawWinningBand = true;
        public bool drawSupportBounds = true;
        public bool drawFloorPlane = true;
        public bool drawFloorCenter = true;
        public bool drawLocalAxes = true;

        [Header("Sizing")]
        [Min(0.001f)] public float centerMarkerRadius = 0.08f;
        [Min(0.001f)] public float crossHalfSize = 0.20f;
        [Min(0.001f)] public float axisLength = 0.50f;
        [Min(0.0f)] public float planeInset = 0.00f;

        [Header("Colors")]
        public Color analyzedBoundsColor = new Color(0.20f, 0.70f, 1.00f, 1.00f);
        public Color winningBandWireColor = new Color(1.00f, 0.85f, 0.20f, 1.00f);
        public Color winningBandFillColor = new Color(1.00f, 0.85f, 0.20f, 0.12f);
        public Color supportBoundsColor = new Color(0.20f, 1.00f, 0.35f, 1.00f);
        public Color floorPlaneColor = new Color(1.00f, 0.30f, 0.90f, 1.00f);
        public Color floorCenterColor = new Color(1.00f, 0.15f, 0.15f, 1.00f);

        [Header("Editor Labels")]
        public bool drawLabels = true;

        public SplatFloorEstimate Estimate => estimate;

        public void SetEstimate(SplatFloorEstimate value)
        {
            estimate = value;
        }

        public void ClearEstimate()
        {
            estimate = null;
        }

        public static SplatFloorDebugGizmos AttachTo(GameObject go, SplatFloorEstimate value)
        {
            if (go == null)
                return null;

            var gizmos = go.GetComponent<SplatFloorDebugGizmos>();
            if (gizmos == null)
                gizmos = go.AddComponent<SplatFloorDebugGizmos>();

            gizmos.localSpace = go.transform;
            gizmos.SetEstimate(value);
            return gizmos;
        }

        void Reset()
        {
            if (localSpace == null)
                localSpace = transform;
        }

        void OnDrawGizmos()
        {
            if (!drawAlways)
                return;

            DrawEstimateGizmos(selectedPass: false);
        }

        void OnDrawGizmosSelected()
        {
            if (!drawWhenSelected)
                return;

            DrawEstimateGizmos(selectedPass: true);
        }

        void DrawEstimateGizmos(bool selectedPass)
        {
            if (!enabled)
                return;

            if (estimate == null || !estimate.success)
                return;

            Transform space = localSpace != null ? localSpace : transform;

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;

            Gizmos.matrix = space.localToWorldMatrix;

            if (drawLocalAxes)
                DrawAxes();

            if (drawAnalyzedBounds)
                DrawBounds(estimate.analyzedBounds, analyzedBoundsColor);

            if (drawWinningBand)
                DrawWinningBand(estimate.analyzedBounds, estimate.winningBandY);

            if (drawSupportBounds)
                DrawBounds(estimate.supportBounds, supportBoundsColor);

            if (drawFloorPlane)
                DrawFloorPlane(estimate.analyzedBounds, estimate.estimatedFloorY);

            if (drawFloorCenter)
                DrawFloorCenter(estimate.floorCenterXZ, estimate.estimatedFloorY);

            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;

#if UNITY_EDITOR
            if (drawLabels)
                DrawLabels(space, selectedPass);
#endif
        }

        void DrawAxes()
        {
            Vector3 o = Vector3.zero;

            Gizmos.color = Color.red;
            Gizmos.DrawLine(o, o + Vector3.right * axisLength);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(o, o + Vector3.up * axisLength);

            Gizmos.color = Color.blue;
            Gizmos.DrawLine(o, o + Vector3.forward * axisLength);
        }

        void DrawBounds(Bounds b, Color color)
        {
            if (b.size == Vector3.zero)
                return;

            Gizmos.color = color;
            Gizmos.DrawWireCube(b.center, b.size);
        }

        void DrawWinningBand(Bounds analyzedBounds, Vector2 winningBandY)
        {
            float yMin = winningBandY.x;
            float yMax = winningBandY.y;
            float h = yMax - yMin;

            if (h <= 0f)
                return;

            Bounds band = analyzedBounds;
            band.SetMinMax(
                new Vector3(analyzedBounds.min.x, yMin, analyzedBounds.min.z),
                new Vector3(analyzedBounds.max.x, yMax, analyzedBounds.max.z)
            );

            Gizmos.color = winningBandFillColor;
            Gizmos.DrawCube(band.center, band.size);

            Gizmos.color = winningBandWireColor;
            Gizmos.DrawWireCube(band.center, band.size);
        }

        void DrawFloorPlane(Bounds analyzedBounds, float floorY)
        {
            float minX = analyzedBounds.min.x + planeInset;
            float maxX = analyzedBounds.max.x - planeInset;
            float minZ = analyzedBounds.min.z + planeInset;
            float maxZ = analyzedBounds.max.z - planeInset;

            if (maxX < minX)
            {
                float mid = 0.5f * (minX + maxX);
                minX = mid;
                maxX = mid;
            }

            if (maxZ < minZ)
            {
                float mid = 0.5f * (minZ + maxZ);
                minZ = mid;
                maxZ = mid;
            }

            Vector3 a = new Vector3(minX, floorY, minZ);
            Vector3 b = new Vector3(maxX, floorY, minZ);
            Vector3 c = new Vector3(maxX, floorY, maxZ);
            Vector3 d = new Vector3(minX, floorY, maxZ);

            Gizmos.color = floorPlaneColor;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }

        void DrawFloorCenter(Vector2 floorCenterXZ, float floorY)
        {
            Vector3 p = new Vector3(floorCenterXZ.x, floorY, floorCenterXZ.y);

            Gizmos.color = floorCenterColor;
            Gizmos.DrawSphere(p, centerMarkerRadius);

            Gizmos.DrawLine(
                p + Vector3.right * crossHalfSize,
                p - Vector3.right * crossHalfSize
            );

            Gizmos.DrawLine(
                p + Vector3.forward * crossHalfSize,
                p - Vector3.forward * crossHalfSize
            );

            Gizmos.DrawLine(
                p + Vector3.up * crossHalfSize,
                p - Vector3.up * crossHalfSize
            );
        }

#if UNITY_EDITOR
        void DrawLabels(Transform space, bool selectedPass)
        {
            if (estimate == null || !estimate.success)
                return;

            Vector3 worldFloorCenter = space.TransformPoint(
                new Vector3(estimate.floorCenterXZ.x, estimate.estimatedFloorY, estimate.floorCenterXZ.y)
            );

            Vector3 worldBandTop = space.TransformPoint(
                new Vector3(
                    estimate.analyzedBounds.center.x,
                    estimate.winningBandY.y,
                    estimate.analyzedBounds.center.z
                )
            );

            var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            style.normal.textColor = floorCenterColor;

            UnityEditor.Handles.Label(
                worldFloorCenter + Vector3.up * 0.12f,
                $"Floor Y: {estimate.estimatedFloorY:F3}\nFloor Center XZ: ({estimate.floorCenterXZ.x:F3}, {estimate.floorCenterXZ.y:F3})",
                style
            );

            style.normal.textColor = winningBandWireColor;
            UnityEditor.Handles.Label(
                worldBandTop + Vector3.up * 0.12f,
                $"Winning Band: [{estimate.winningBandY.x:F3}, {estimate.winningBandY.y:F3}]",
                style
            );

            style.normal.textColor = supportBoundsColor;
            UnityEditor.Handles.Label(
                space.TransformPoint(estimate.supportBounds.center + Vector3.up * (estimate.supportBounds.extents.y + 0.12f)),
                $"Support Count: {estimate.supportCount}\nSupport Area XZ: {estimate.supportAreaXZ:F3}",
                style
            );
        }
#endif
    }
}