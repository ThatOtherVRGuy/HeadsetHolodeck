using UnityEngine;
using UnityEngine.Rendering;

namespace Holodeck.Effects
{
    /// <summary>
    /// Builds a preview rig for seven human chakra vortices.
    /// Back-facing cones collect into the body; front-facing cones emit out from it.
    /// </summary>
    public static class ChakraVortexRigBuilder
    {
        private const string RigName = "ChakraVortexRig";

        private readonly struct ChakraSpec
        {
            public ChakraSpec(string name, Color color, Vector3 localPosition, float radius, float length, bool vertical, bool crown)
            {
                Name = name;
                Color = color;
                LocalPosition = localPosition;
                Radius = radius;
                Length = length;
                Vertical = vertical;
                Crown = crown;
            }

            public readonly string Name;
            public readonly Color Color;
            public readonly Vector3 LocalPosition;
            public readonly float Radius;
            public readonly float Length;
            public readonly bool Vertical;
            public readonly bool Crown;
        }

        public static GameObject BuildPreviewRig()
        {
            var existing = GameObject.Find(RigName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            var rig = new GameObject(RigName);
            rig.transform.position = Vector3.zero;
            rig.transform.rotation = Quaternion.identity;
            rig.transform.localScale = Vector3.one;

            var specs = new[]
            {
                new ChakraSpec("Crown", new Color(0.96f, 0.92f, 1f, 0.95f), new Vector3(0f, 1.85f, 0f), 0.18f, 0.72f, true, true),
                new ChakraSpec("ThirdEye", new Color(0.63f, 0.22f, 1f, 0.95f), new Vector3(0f, 1.55f, 0f), 0.20f, 0.88f, false, false),
                new ChakraSpec("Throat", new Color(0.16f, 0.58f, 1f, 0.95f), new Vector3(0f, 1.24f, 0f), 0.19f, 0.76f, false, false),
                new ChakraSpec("Heart", new Color(0.12f, 0.86f, 0.32f, 0.95f), new Vector3(0f, 0.82f, 0f), 0.24f, 1.02f, false, false),
                new ChakraSpec("SolarPlexus", new Color(1f, 0.86f, 0.12f, 0.95f), new Vector3(0f, 0.35f, 0f), 0.23f, 0.92f, false, false),
                new ChakraSpec("Sacral", new Color(1f, 0.39f, 0.08f, 0.95f), new Vector3(0f, -0.12f, 0f), 0.22f, 0.82f, false, false),
                new ChakraSpec("Root", new Color(1f, 0.10f, 0.08f, 0.95f), new Vector3(0f, -0.72f, 0f), 0.22f, 0.82f, true, false),
            };

            foreach (var spec in specs)
            {
                CreateChakra(rig.transform, spec);
            }

            Debug.Log("[ChakraVortexRigBuilder] Built ChakraVortexRig preview with seven spinning chakra vortices.");
            return rig;
        }

        private static void CreateChakra(Transform parent, ChakraSpec spec)
        {
            var anchor = new GameObject(spec.Name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = spec.LocalPosition;

            CreateCoreGlow(anchor.transform, spec);

            if (spec.Vertical)
            {
                var direction = spec.Crown ? Vector3.up : Vector3.down;
                CreateCone(anchor.transform, spec, "VerticalVortex", direction, true);
                CreateRing(anchor.transform, spec, "VerticalVortexMouthRing", direction, spec.Length);
                return;
            }

            CreateCone(anchor.transform, spec, "FrontEmissionCone", Vector3.forward, true);
            CreateCone(anchor.transform, spec, "BackCollectionCone", Vector3.back, false);
            CreateRing(anchor.transform, spec, "FrontEmissionMouthRing", Vector3.forward, spec.Length);
            CreateRing(anchor.transform, spec, "BackCollectionMouthRing", Vector3.back, spec.Length);
        }

        private static void CreateCoreGlow(Transform parent, ChakraSpec spec)
        {
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "CoreGlow";
            core.transform.SetParent(parent, false);
            core.transform.localScale = Vector3.one * spec.Radius * 0.55f;

            var collider = core.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            var renderer = core.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateMaterial(spec.Name + "_CoreGlow_Mat", spec.Color, 1.25f);
        }

        private static void CreateCone(Transform parent, ChakraSpec spec, string name, Vector3 direction, bool emitAwayFromBody)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            var spinner = go.AddComponent<ChakraVortexSpinner>();
            spinner.Axis = Vector3.forward;
            spinner.DegreesPerSecond = emitAwayFromBody ? 24f : -24f;

            CreateConeShell(go, spec, name, emitAwayFromBody);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 3.5f;
            main.loop = true;
            main.startLifetime = emitAwayFromBody ? new ParticleSystem.MinMaxCurve(1.1f, 1.8f) : new ParticleSystem.MinMaxCurve(0.8f, 1.35f);
            main.startSpeed = emitAwayFromBody ? new ParticleSystem.MinMaxCurve(0.12f, 0.28f) : new ParticleSystem.MinMaxCurve(-0.34f, -0.16f);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.Radius * 0.08f, spec.Radius * 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(spec.Color.r, spec.Color.g, spec.Color.b, 0.22f), spec.Color);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 280;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 90;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = spec.Radius;
            shape.length = spec.Length;
            shape.arc = 360f;
            shape.randomDirectionAmount = 0.12f;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(emitAwayFromBody ? 0.55f : -0.55f);
            velocity.radial = new ParticleSystem.MinMaxCurve(emitAwayFromBody ? 0.12f : -0.18f);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(CreateFadeGradient(spec.Color));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.45f, 1f),
                new Keyframe(1f, 0.05f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-180f * Mathf.Deg2Rad, 180f * Mathf.Deg2Rad);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = CreateMaterial(spec.Name + "_" + name + "_Mat", spec.Color, 1.6f);

            ps.Play();
        }

        private static void CreateConeShell(GameObject coneObject, ChakraSpec spec, string name, bool emitAwayFromBody)
        {
            var meshFilter = coneObject.AddComponent<MeshFilter>();
            var meshRenderer = coneObject.AddComponent<MeshRenderer>();

            const int segments = 64;
            var vertices = new Vector3[segments + 2];
            var triangles = new int[segments * 6];

            vertices[0] = Vector3.zero;
            for (var i = 0; i < segments; i++)
            {
                var t = (float)i / segments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(t) * spec.Radius, Mathf.Sin(t) * spec.Radius, spec.Length);
            }

            vertices[segments + 1] = new Vector3(0f, 0f, spec.Length);

            var triangleIndex = 0;
            for (var i = 0; i < segments; i++)
            {
                var next = (i + 1) % segments;

                triangles[triangleIndex++] = 0;
                triangles[triangleIndex++] = i + 1;
                triangles[triangleIndex++] = next + 1;

                triangles[triangleIndex++] = segments + 1;
                triangles[triangleIndex++] = next + 1;
                triangles[triangleIndex++] = i + 1;
            }

            var mesh = new Mesh
            {
                name = spec.Name + "_" + name + "_ConeShell"
            };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;

            var shellColor = new Color(spec.Color.r, spec.Color.g, spec.Color.b, emitAwayFromBody ? 0.11f : 0.07f);
            meshRenderer.sharedMaterial = CreateTransparentMaterial(spec.Name + "_" + name + "_Shell_Mat", shellColor, 1.15f);
        }

        private static Gradient CreateFadeGradient(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 0.72f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.72f, 0.18f),
                    new GradientAlphaKey(0.18f, 0.82f),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        private static void CreateRing(Transform parent, ChakraSpec spec, string name, Vector3 direction, float distance)
        {
            var ring = new GameObject(name);
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = direction.normalized * distance;
            ring.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            var line = ring.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 96;
            line.widthMultiplier = 0.004f;
            line.sharedMaterial = CreateMaterial(spec.Name + "_" + name + "_Mat", spec.Color, 1.8f);
            line.startColor = spec.Color;
            line.endColor = spec.Color;

            for (var i = 0; i < line.positionCount; i++)
            {
                var t = (float)i / line.positionCount * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(t) * spec.Radius, Mathf.Sin(t) * spec.Radius, 0f));
            }

            var spinner = ring.AddComponent<ChakraVortexSpinner>();
            spinner.Axis = Vector3.forward;
            spinner.DegreesPerSecond = direction.z < 0f ? -48f : 48f;
        }

        private static Material CreateMaterial(string name, Color color, float intensity)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader)
            {
                name = name
            };

            var boosted = new Color(
                Mathf.Clamp01(color.r * intensity),
                Mathf.Clamp01(color.g * intensity),
                Mathf.Clamp01(color.b * intensity),
                color.a);

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", boosted);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", boosted);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 1f);
            }

            material.renderQueue = 3000;
            return material;
        }

        private static Material CreateTransparentMaterial(string name, Color color, float intensity)
        {
            var material = CreateMaterial(name, color, intensity);
            material.SetOverrideTag("RenderType", "Transparent");

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 1f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.One);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)CullMode.Off);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }
    }

    public sealed class ChakraVortexSpinner : MonoBehaviour
    {
        public Vector3 Axis = Vector3.forward;
        public float DegreesPerSecond = 48f;

        private void Update()
        {
            transform.Rotate(Axis.normalized, DegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
