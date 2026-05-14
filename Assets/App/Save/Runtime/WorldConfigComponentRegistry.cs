// Assets/App/Save/Runtime/WorldConfigComponentRegistry.cs
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Holodeck.Save
{
    public static class WorldConfigComponentRegistry
    {
        static readonly Dictionary<string, IComponentSerializer> _serializers
            = new Dictionary<string, IComponentSerializer>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register a serializer. Called from [RuntimeInitializeOnLoadMethod] in each serializer file.</summary>
        public static void Register(IComponentSerializer serializer)
        {
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));
            _serializers[serializer.TypeName] = serializer;
        }

        /// <summary>
        /// Run all registered serializers against <paramref name="go"/> and return
        /// any non-null results as a SavedComponent list.
        /// </summary>
        public static List<SavedComponent> SaveAll(GameObject go)
        {
            var result = new List<SavedComponent>();
            foreach (IComponentSerializer s in _serializers.Values)
            {
                JObject data = s.Save(go);
                if (data != null)
                    result.Add(new SavedComponent { type = s.TypeName, data = data });
            }
            return result;
        }

        /// <summary>Apply each SavedComponent to <paramref name="go"/> using the registered serializer.</summary>
        public static void RestoreAll(GameObject go, List<SavedComponent> components, RestorationContext ctx)
        {
            if (components == null) return;
            foreach (SavedComponent c in components)
            {
                if (string.IsNullOrEmpty(c.type))
                {
                    Debug.LogWarning("[WorldConfigComponentRegistry] SavedComponent has null or empty type; skipping.");
                    continue;
                }
                if (_serializers.TryGetValue(c.type, out IComponentSerializer s))
                    s.Restore(go, c.data, ctx);
                else
                    Debug.LogWarning($"[WorldConfigComponentRegistry] No serializer registered for type '{c.type}'");
            }
        }

        /// <summary>Used only in tests — clears all registered serializers.</summary>
        public static void ClearForTesting() => _serializers.Clear();

        /// <summary>Used only in tests — returns the count of registered serializers.</summary>
        public static int CountForTesting() => _serializers.Count;
    }
}
