using System.Collections.Generic;
using UnityEngine;

namespace SpeechIntent
{
    public class SceneSemanticContextProvider : MonoBehaviour
    {
        [TextArea(2, 5)]
        public string currentWorldDescription = "Default world";

        public string currentLightingPreset = "day";
        public bool staticWorldActive = false;

        [Header("Memory / Resolution")]
        public InteractionMemory interactionMemory;
        public SceneEntityResolver entityResolver;

        [Header("Hints for NLU")]
        public List<string> availablePlaceableObjects = new List<string>();
        public List<string> visibleUiPanels = new List<string>();
        public List<string> aliasesForUi = new List<string>() { "arch", "exit", "menu", "panel" };

        [Header("Discovery")]
        public bool includeTrackableSceneObjects = true;
        [Range(0, 64)] public int maxTrackableNames = 24;

        public SceneSemanticSnapshot CaptureSnapshot()
        {
            SceneSemanticSnapshot snapshot = new SceneSemanticSnapshot
            {
                current_world_description = ResolveWorldDescription(),
                current_world_root_name = interactionMemory != null && interactionMemory.currentWorldRoot != null
                    ? interactionMemory.currentWorldRoot.name
                    : string.Empty,
                current_lighting_preset = currentLightingPreset,
                static_world_active = staticWorldActive,
                last_created_target_name = interactionMemory != null && interactionMemory.lastCreatedObject != null
                    ? interactionMemory.lastCreatedObject.name
                    : string.Empty,
                last_interacted_target_name = interactionMemory != null && interactionMemory.lastInteractedTarget != null
                    ? interactionMemory.lastInteractedTarget.name
                    : string.Empty,
                available_placeable_objects = new List<string>(availablePlaceableObjects),
                visible_ui_panels = new List<string>(visibleUiPanels),
                aliases_for_ui = new List<string>(aliasesForUi),
                named_scene_objects = includeTrackableSceneObjects && entityResolver != null
                    ? entityResolver.CollectTrackableNames(maxTrackableNames)
                    : new List<string>()
            };

            return snapshot;
        }

        private string ResolveWorldDescription()
        {
            if (interactionMemory != null && !string.IsNullOrWhiteSpace(interactionMemory.currentWorldDescription))
            {
                return interactionMemory.currentWorldDescription;
            }

            return currentWorldDescription;
        }
    }
}
