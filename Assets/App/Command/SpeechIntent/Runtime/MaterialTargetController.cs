using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent
{
    public class MaterialTargetController : MonoBehaviour
    {
        public RuntimeMaterialCatalog materialCatalog;
        public SceneEntityResolver entityResolver;
        public InteractionMemory interactionMemory;

        [Tooltip("When true, material changes preserve shared materials by assigning a catalog material if another object uses the current material.")]
        public bool forkIfShared = true;
        public string LastFailureMessage { get; private set; } = "";

        public bool TryApplyMaterial(VoiceIntentCommand command, SpatialSnapshot spatial, out List<GameObject> targets)
        {
            LastFailureMessage = "";
            targets = ResolveTargets(command, spatial);
            if (targets.Count == 0)
                return false;

            RuntimeMaterialCatalog catalog = ResolveCatalog();
            if (catalog == null)
            {
                LastFailureMessage = "Material catalog not found.";
                return false;
            }

            string prompt = FirstNonEmpty(command.material_prompt, command.world_prompt, command.transcript);
            if (!catalog.TryParseDescriptor(prompt, out RuntimeMaterialDescriptor descriptor))
            {
                LastFailureMessage = "What material?";
                return false;
            }

            foreach (GameObject target in targets)
            {
                if (target == null)
                    continue;

                catalog.ApplyTo(target, descriptor, forkIfShared);
                if (interactionMemory != null)
                    interactionMemory.RegisterInteraction(target);
            }

            return true;
        }

        RuntimeMaterialCatalog ResolveCatalog()
        {
            if (materialCatalog == null)
                materialCatalog = FindFirstObjectByType<RuntimeMaterialCatalog>(FindObjectsInactive.Include);
            return materialCatalog;
        }

        List<GameObject> ResolveTargets(VoiceIntentCommand command, SpatialSnapshot spatial)
        {
            var targets = new List<GameObject>();
            if (command == null)
                return targets;

            if (entityResolver != null)
            {
                SceneTargetResolution resolution = entityResolver.ResolveTargets(command, spatial);
                if (resolution.status == SceneTargetResolutionStatus.Ambiguous ||
                    resolution.status == SceneTargetResolutionStatus.None)
                {
                    LastFailureMessage = resolution.message;
                    return targets;
                }

                targets.AddRange(resolution.targets);
                return targets;
            }

            GameObject target = null;
            if (interactionMemory != null)
                target = interactionMemory.GetLastCreatedOrInteracted();

            if (target != null)
                targets.Add(target);

            return targets;
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return "";
        }
    }
}
