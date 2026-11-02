using System;
using System.Reflection;
using GaussianSplatting.Runtime;
using SpeechIntent;
using UnityEngine;
using WorldLabs.Runtime;

namespace HeadsetHolodeck.EditorTests
{
    public static class ViewModeControllerBatchTests
    {
        public static void ShowCurrentWorldRootReactivatesLoadedRendererWithoutInteractionMemory()
        {
            GameObject parentGo = null;
            GameObject managerGo = null;
            GameObject rendererGo = null;
            GameObject viewGo = null;

            try
            {
                parentGo = new GameObject("WorldParent");
                managerGo = new GameObject("WorldManager");
                rendererGo = new GameObject("World_Test");
                viewGo = new GameObject("ViewModeController");

                rendererGo.transform.SetParent(parentGo.transform, false);

                var manager = managerGo.AddComponent<WorldLabsWorldManager>();
                manager.worldParent = parentGo.transform;

                var renderer = rendererGo.AddComponent<GaussianSplatRenderer>();
                manager.RegisterExternalWorld("world-a", renderer);

                var viewModeController = viewGo.AddComponent<ViewModeController>();
                viewModeController.worldManager = manager;
                viewModeController.interactionMemory = null;

                parentGo.SetActive(false);
                rendererGo.SetActive(false);

                MethodInfo showMethod = typeof(ViewModeController).GetMethod(
                    "ShowCurrentWorldRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (showMethod == null)
                    throw new MissingMethodException(nameof(ViewModeController), "ShowCurrentWorldRoot");

                MethodInfo onLoadedMethod = typeof(ViewModeController).GetMethod(
                    "OnWorldLoaded",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (onLoadedMethod == null)
                    throw new MissingMethodException(nameof(ViewModeController), "OnWorldLoaded");

                onLoadedMethod.Invoke(viewModeController, new object[] { "world-a", renderer });

                showMethod.Invoke(viewModeController, null);

                if (!parentGo.activeSelf)
                    throw new Exception("Expected world parent to be active after ShowCurrentWorldRoot.");

                if (!rendererGo.activeSelf)
                    throw new Exception("Expected loaded splat renderer to be active even when InteractionMemory.currentWorldRoot is not set yet.");

                Debug.Log("[ViewModeControllerBatchTests] ShowCurrentWorldRootReactivatesLoadedRendererWithoutInteractionMemory passed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewGo);
                UnityEngine.Object.DestroyImmediate(rendererGo);
                UnityEngine.Object.DestroyImmediate(managerGo);
                UnityEngine.Object.DestroyImmediate(parentGo);
            }
        }
    }
}
