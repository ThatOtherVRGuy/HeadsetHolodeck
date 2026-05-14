// Assets/App/Save/Runtime/AudioClipPathHolder.cs
using UnityEngine;

namespace Holodeck.Save
{
    /// <summary>
    /// Lightweight component that stores the file path of a loaded AudioClip.
    /// AudioSourceSerializer reads this path on Save; WorldConfigRestorer uses
    /// absolutePath to reload the clip on Restore.
    /// </summary>
    public class AudioClipPathHolder : MonoBehaviour
    {
        [Tooltip("Relative path from config folder, e.g. ../CachedWorlds/sound.mp3")]
        public string clipPath = "";
        [Tooltip("Resolved absolute device path — populated by AudioSourceSerializer.Restore")]
        public string absolutePath = "";
    }
}
