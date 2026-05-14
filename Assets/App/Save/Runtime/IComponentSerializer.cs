// Assets/App/Save/Runtime/IComponentSerializer.cs
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Holodeck.Save
{
    public interface IComponentSerializer
    {
        /// <summary>Unique string identifying this component type in world.json.</summary>
        string TypeName { get; }

        /// <summary>
        /// Snapshot the relevant component(s) on <paramref name="go"/>.
        /// Returns null if this serializer is not applicable to the GameObject.
        /// </summary>
        JObject Save(GameObject go);

        /// <summary>Apply saved <paramref name="data"/> to <paramref name="go"/>.</summary>
        void Restore(GameObject go, JObject data, RestorationContext ctx);
    }
}
