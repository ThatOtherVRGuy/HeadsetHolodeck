using System.Collections;
using UnityEngine;

namespace Holodeck.Direct
{
    public sealed class ObjectGenerationSpinnerController : MonoBehaviour
    {
        [Header("Motion")]
        public Vector3 rotationDegreesPerSecond = new Vector3(0f, 35f, 0f);

        [Header("Visuals")]
        public Color sparkleColor = new Color(0.35f, 0.8f, 1f, 1f);
        public Color accentColor = new Color(1f, 0.65f, 0.1f, 1f);

        [Header("Dismissal")]
        [Tooltip("Extra time after emission stops before the spinner object is destroyed.")]
        public float dismissalTailSeconds = 0.25f;
        public bool useParticleLifetimeForDismissal = true;

        bool _dismissed;
        Coroutine _dismissCoroutine;

        public static ObjectGenerationSpinnerController CreateRuntimeSpinner(
            Vector3 position,
            Quaternion rotation,
            float diameterMeters,
            ObjectGenerationSpinnerController prefab)
        {
            float diameter = Mathf.Max(0.05f, diameterMeters);
            ObjectGenerationSpinnerController spinner;
            if (prefab != null)
            {
                spinner = Instantiate(prefab, position, rotation);
            }
            else
            {
                GameObject go = new GameObject("ObjectGenerationSpinner");
                spinner = go.AddComponent<ObjectGenerationSpinnerController>();
                spinner.BuildDefaultVisuals();
                go.transform.SetPositionAndRotation(position, rotation);
            }

            spinner.name = "ObjectGenerationSpinner";
            spinner.transform.localScale = Vector3.one * diameter;
            spinner.Play();
            return spinner;
        }

        public void Play()
        {
            _dismissed = false;
            if (_dismissCoroutine != null)
            {
                StopCoroutine(_dismissCoroutine);
                _dismissCoroutine = null;
            }

            ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                system.gameObject.SetActive(true);
                ParticleSystem.MainModule main = system.main;
                main.prewarm = false;
                system.Clear(true);
                system.Play(true);
            }
        }

        public void Dismiss()
        {
            if (_dismissed)
                return;

            _dismissed = true;
            ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (Application.isPlaying)
                _dismissCoroutine = StartCoroutine(DestroyAfterParticlesFade());
            else
                DestroyObject(gameObject);
        }

        IEnumerator DestroyAfterParticlesFade()
        {
            yield return new WaitForSecondsRealtime(ResolveDismissDelaySeconds());
            DestroyObject(gameObject);
        }

        void Update()
        {
            if (_dismissed)
                return;

            transform.Rotate(rotationDegreesPerSecond * Time.deltaTime, Space.Self);
        }

        void Reset()
        {
            if (GetComponentInChildren<ParticleSystem>() == null)
                BuildDefaultVisuals();
        }

        public void BuildDefaultVisuals()
        {
            foreach (ParticleSystem existing in GetComponentsInChildren<ParticleSystem>(true))
                DestroyObject(existing.gameObject);

            CreateParticleChild("SparkleShell", sparkleColor, 110f, 0.018f, 1.1f, 0.08f);
            CreateParticleChild("AmberOrbit", accentColor, 35f, 0.026f, 0.9f, 0.16f);
        }

        void CreateParticleChild(string childName, Color color, float emissionRate, float particleSize, float lifetime, float speed)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            ParticleSystem ps = child.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = particleSize;
            main.startColor = color;
            main.maxParticles = 512;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = emissionRate;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            shape.radiusThickness = 0.08f;
            shape.randomDirectionAmount = 0.18f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(Color.white, 0.45f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.15f),
                    new GradientAlphaKey(color.a, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = CreateParticleMaterial(color);
        }

        static Material CreateParticleMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader)
            {
                name = "ObjectGenerationSpinnerParticle_Material",
                color = color
            };
            return material;
        }

        float ResolveDismissDelaySeconds()
        {
            float delay = Mathf.Max(0f, dismissalTailSeconds);
            if (!useParticleLifetimeForDismissal)
                return delay;

            foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                delay = Mathf.Max(delay, main.startLifetime.constantMax + dismissalTailSeconds);
            }

            return delay;
        }

        static void DestroyObject(Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
