using System;
using UnityEngine;

namespace SpeechIntent
{
    public enum RuntimeLightKind
    {
        Point = 0,
        Spot = 1,
        Directional = 2,
        Ambient = 3
    }

    public class RuntimeLightIdentity : MonoBehaviour
    {
        public RuntimeLightKind kind = RuntimeLightKind.Point;
        public bool isRuntimeLight = true;
        public bool isSun = false;
        public bool isFlashlight = false;

        public static event Action<RuntimeLightIdentity> RuntimeSunDestroyed;

        void OnDestroy()
        {
            if (isSun)
                RuntimeSunDestroyed?.Invoke(this);
        }
    }
}
