# Chakra Vortex Particle Rig Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a previewable Unity particle rig named `ChakraVortexRig` with seven spinning chakra vortices that collect energy from the back and emit energy from the front.

**Architecture:** Use regular Unity GameObjects, Materials, ParticleSystems, and a tiny runtime/editor helper script. A script creates the rig procedurally so the first pass is repeatable, inspectable, and easy to tune from the Editor. The active scene receives the generated preview object, and a prefab can be created later if the look is approved.

**Tech Stack:** Unity 6000.2.10f1, C#, UnityEngine.ParticleSystem, IvanMurzak Unity MCP `script-update-or-create`, `assets-refresh`, and `script-execute`.

---

## File Structure

- Create `Assets/App/Scripts/Effects/ChakraVortexRigBuilder.cs`
  - Owns the procedural creation of the chakra preview rig.
  - Creates additive-style materials.
  - Creates cone particle systems, core glows, and simple ring line renderers.
  - Can be invoked from editor execution without needing a permanent scene dependency.

- Create scene object `ChakraVortexRig`
  - Temporary preview instance in the currently open Unity scene.
  - Not saved to the scene unless explicitly requested later.

## Task 1: Add the Chakra Vortex Rig Builder Script

**Files:**
- Create: `Assets/App/Scripts/Effects/ChakraVortexRigBuilder.cs`

- [ ] **Step 1: Create the script directory**

Run:

```bash
mkdir -p Assets/App/Scripts/Effects
```

Expected: Directory exists.

- [ ] **Step 2: Create `ChakraVortexRigBuilder.cs`**

Write this complete file:

```csharp
using System;
using UnityEngine;

namespace Holodeck.Effects
{
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
                UnityEngine.Object.DestroyImmediate(existing);
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
                UnityEngine.Object.DestroyImmediate(collider);
            }

            var renderer = core.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateMaterial(spec.Name + "_CoreGlow_Mat", spec.Color, 1.25f);
        }

        private static void CreateCone(Transform parent, ChakraSpec spec, string name, Vector3 direction, bool emitAwayFromBody)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);

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
            line.widthMultiplier = 0.012f;
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
```

- [ ] **Step 3: Refresh Unity assets**

Run Ivan MCP `assets-refresh` with `ForceSynchronousImport`.

Expected: Unity compiles with no new C# compiler errors.

## Task 2: Generate the Preview Rig in the Active Scene

**Files:**
- Use: `Assets/App/Scripts/Effects/ChakraVortexRigBuilder.cs`
- Modify scene memory only: currently open Unity scene

- [ ] **Step 1: Execute the builder in Unity**

Run Ivan MCP `script-execute` with this body:

```csharp
Holodeck.Effects.ChakraVortexRigBuilder.BuildPreviewRig();
```

Set `isMethodBody` to `true`.

Expected: Unity creates a root GameObject named `ChakraVortexRig`.

- [ ] **Step 2: Inspect the generated hierarchy**

Run Ivan MCP `gameobject-find` for `ChakraVortexRig` with `hierarchyDepth` set to `2` and `includeComponents` set to `true`.

Expected:

- Root object exists.
- Seven child anchors exist.
- Torso/head anchors have `CoreGlow`, `FrontEmissionCone`, `BackCollectionCone`, `FrontEmissionMouthRing`, and `BackCollectionMouthRing`.
- Crown and Root have `CoreGlow`, `VerticalVortex`, and `VerticalVortexMouthRing`.

- [ ] **Step 3: Capture a scene-view screenshot**

Run Ivan MCP `screenshot-scene-view`.

Expected: Screenshot shows a vertical human-scale chakra rig with colored cone/ring particle systems.

## Task 3: Verify Console and Hand Off for Visual Review

**Files:**
- Read only: Unity console

- [ ] **Step 1: Read recent console errors**

Run Ivan MCP `console-get-logs` with `logTypeFilter = "Error"` and `lastMinutes = 5`.

Expected: No new errors from `ChakraVortexRigBuilder`.

- [ ] **Step 2: Read recent console exceptions**

Run Ivan MCP `console-get-logs` with `logTypeFilter = "Exception"` and `lastMinutes = 5`.

Expected: No new exceptions from `ChakraVortexRigBuilder`.

- [ ] **Step 3: Report the preview status**

Tell the user:

- Whether the rig was created.
- Whether console errors appeared.
- That the scene is not saved unless the user asks.
- What to inspect in Unity: side view should show conical front/back vortices; front view should show seven colored chakra centers.
