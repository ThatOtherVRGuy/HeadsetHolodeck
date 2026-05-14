namespace WorldLabs.Runtime
{
    /// <summary>
    /// Implemented by WorldConfigStore (Assembly-CSharp) and consumed by WorldBrowserController
    /// (WorldLabs assembly). Keeping the interface inside WorldLabs breaks the circular
    /// dependency that would arise from a direct WorldConfigStore reference in a package asmdef.
    /// </summary>
    public interface IWorldBookmarkProvider
    {
        bool HasConfigForWorldId(string worldId);
    }
}
