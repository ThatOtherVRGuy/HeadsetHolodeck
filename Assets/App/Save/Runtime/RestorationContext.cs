// Assets/App/Save/Runtime/RestorationContext.cs
namespace Holodeck.Save
{
    /// <summary>
    /// Passed to IComponentSerializer.Restore so serializers can resolve
    /// relative asset paths (e.g. ../CachedWorlds/sound.mp3) to absolute paths.
    /// </summary>
    public class RestorationContext
    {
        /// <summary>Absolute path to the config folder containing world.json.</summary>
        public string ConfigFolderPath;
        public WorldConfig Config;
    }
}
