// Assets/App/Shared/Save/Runtime/AssemblyInfo.cs
using System.Runtime.CompilerServices;

// The save file protocol in WorldConfigFile (temporary write, atomic replacement, recovery backup,
// writer lock) is internal so no other assembly can bypass it. The save fixtures need to drive its
// individual failure stages, so the test assembly is granted access instead of widening the
// runtime surface.
[assembly: InternalsVisibleTo("Holodeck.Save.Tests")]
