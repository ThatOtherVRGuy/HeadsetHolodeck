using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Holodeck.Editor
{
    /// <summary>
    /// Applies the shared HeadsetHolodeck icon art to Unity's Android player settings.
    ///
    /// The art is authored once for both headset targets: the Meta Quest launcher and the PICO
    /// dock both come from the Android icon slots in the APK, so only the store listings
    /// (under docs/branding) differ per platform. Re-exporting the PNGs from
    /// `docs/branding/app-icon/build_app_icons.py` and then running this is the whole update path.
    ///
    /// Slots are filled by size so the legacy (36-192 px), round and adaptive (81-432 px) sets stay
    /// consistent. The adaptive pair is written as layer 0 = background, layer 1 = foreground,
    /// which is the order Unity serializes into `m_BuildTargetPlatformIcons`.
    ///
    /// `ApplyToBothTargets` is the entry point that matters: Unity 6 Build Profiles capture Player
    /// Settings as profile overrides, so the Pico build reads `Pico.asset` while the Meta build
    /// deactivates that profile and reads the global settings (`TargetBuildMenu.BuildMeta` does the
    /// same). Applying only once silently covers one of the two.
    /// </summary>
    public static class AppIconSetup
    {
        const string Root = "Assets/App/UI/Branding/AppIcon";
        const string MenuRoot = "Headset Holodeck/App Icons/";
        const string PicoProfilePath = "Assets/App/Platforms/Pico/Settings/Pico.asset";

        static readonly int[] LegacySizes = { 192, 144, 96, 72, 48 };

        [MenuItem(MenuRoot + "Apply to Both Targets (global + Pico profile)", false, 139)]
        static void ApplyToBothFromMenu()
        {
            ApplyToBothTargets();
        }

        [MenuItem(MenuRoot + "Apply to Current Configuration Only", false, 140)]
        static void ApplyFromMenu()
        {
            Apply();
            Report();
        }

        [MenuItem(MenuRoot + "Report Assigned Icons", false, 141)]
        static void ReportFromMenu()
        {
            Report();
        }

        /// <summary>
        /// Applies the set to the global Player Settings (what the Meta build uses) and to the Pico
        /// build profile, then restores whichever profile was active. Activates no build by itself.
        /// </summary>
        public static void ApplyToBothTargets()
        {
            var previous = BuildProfile.GetActiveBuildProfile();
            try
            {
                BuildProfile.SetActiveBuildProfile(null);
                Apply();

                var pico = AssetDatabase.LoadAssetAtPath<BuildProfile>(PicoProfilePath);
                if (pico == null)
                {
                    Debug.LogWarning($"[AppIcons] Pico profile not found at {PicoProfilePath}; " +
                                     "only the global settings were updated.");
                }
                else
                {
                    BuildProfile.SetActiveBuildProfile(pico);
                    Apply();
                }
            }
            finally
            {
                BuildProfile.SetActiveBuildProfile(previous);
            }

            Report();
        }

        /// <summary>Entry point for `-executeMethod Holodeck.Editor.AppIconSetup.ApplyFromCommandLine`.</summary>
        public static void ApplyFromCommandLine()
        {
            try
            {
                Apply();
                Report();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[AppIcons] Failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        public static void Apply()
        {
            var legacy = LoadLadder("Android/legacy_{0}.png", LegacySizes);
            var round = LoadLadder("Android/round_{0}.png", LegacySizes);
            var foreground = Load("Android/adaptive_foreground_432.png", alpha: true);
            var background = Load("Android/adaptive_background_432.png", alpha: false);

            if (legacy.Count == 0 || round.Count == 0 || foreground == null || background == null)
            {
                throw new InvalidOperationException(
                    "Icon PNGs are missing under " + Root +
                    ". Run docs/branding/app-icon/build_app_icons.py first.");
            }

            SetKind("Legacy", legacy);
            SetKind("Round", round);
            SetAdaptive(foreground, background);

            SaveProjectSettings();
            Debug.Log($"[AppIcons] Applied {legacy.Count} legacy, {round.Count} round and the adaptive " +
                      $"icon set from {Root}.");
        }

        // ------------------------------------------------------------ plumbing ---

        static void SetKind(string kindName, Dictionary<int, Texture2D> bySize)
        {
            var kind = GetAndroidKind(kindName);
            var icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
            for (int i = 0; i < icons.Length; i++)
            {
                var texture = Nearest(bySize, icons[i].width);
                if (texture != null)
                {
                    SetTexture(ref icons[i], texture, 0);
                }
            }

            PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, icons);
        }

        static void SetAdaptive(Texture2D foreground, Texture2D background)
        {
            var kind = GetAndroidKind("Adaptive");
            var icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
            var layered = 0;
            var single = 0;
            for (int i = 0; i < icons.Length; i++)
            {
                SetTexture(ref icons[i], background, 0);
                if (SetTexture(ref icons[i], foreground, 1))
                {
                    layered++;
                }
                else if (SetTexture(ref icons[i], foreground, 0))
                {
                    single++;
                }
            }

            PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, icons);
            Debug.Log($"[AppIcons] Adaptive slots: {layered} two-layer, {single} single-layer.");
        }

        /// <summary>
        /// Sets one layer of a platform icon. Adaptive icons carry two layers while the legacy and
        /// round kinds carry one, and asking Unity for an out-of-range layer throws rather than
        /// returning null, so the probe is part of the call.
        /// </summary>
        static bool SetTexture(ref PlatformIcon icon, Texture2D texture, int layer)
        {
            if (texture == null)
            {
                return false;
            }

            try
            {
                icon.SetTexture(texture, layer);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static Texture2D GetTexture(PlatformIcon icon, int layer)
        {
            try
            {
                return icon.GetTexture(layer);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// `AndroidPlatformIconKind` lives in UnityEditor.Android.Extensions, which is not in the
        /// editor's base directory and is only referenced while Android build support is installed.
        /// It is therefore resolved by scanning the loaded assemblies for the type rather than by an
        /// assembly-qualified name, and kept out of compile-time references so this script still
        /// compiles in an editor without that module. The kinds are static *fields* in Unity 6, so
        /// both fields and properties are probed.
        /// </summary>
        static UnityEditor.PlatformIconKind GetAndroidKind(string name)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("UnityEditor.Android.AndroidPlatformIconKind", false);
                if (type == null)
                {
                    continue;
                }

                if (type.GetField(name, flags)?.GetValue(null) is UnityEditor.PlatformIconKind field)
                {
                    return field;
                }

                if (type.GetProperty(name, flags)?.GetValue(null) is UnityEditor.PlatformIconKind property)
                {
                    return property;
                }
            }

            throw new InvalidOperationException(
                $"Could not resolve Android icon kind '{name}'. Is Android build support installed?");
        }

        static Texture2D Nearest(Dictionary<int, Texture2D> bySize, int wanted)
        {
            var best = bySize.Keys.OrderBy(size => Mathf.Abs(size - wanted)).FirstOrDefault();
            return best == 0 ? null : bySize[best];
        }

        static Dictionary<int, Texture2D> LoadLadder(string pattern, IEnumerable<int> sizes)
        {
            var loaded = new Dictionary<int, Texture2D>();
            foreach (var size in sizes)
            {
                var texture = Load(string.Format(pattern, size), alpha: false);
                if (texture != null)
                {
                    loaded[size] = texture;
                }
            }

            return loaded;
        }

        static Texture2D Load(string relative, bool alpha)
        {
            var path = Root + "/" + relative;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && ConfigureImporter(importer, alpha))
            {
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning("[AppIcons] Missing texture: " + path);
            }

            return texture;
        }

        static bool ConfigureImporter(TextureImporter importer, bool alpha)
        {
            var dirty = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                dirty = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }

            if (importer.alphaIsTransparency != alpha)
            {
                importer.alphaIsTransparency = alpha;
                dirty = true;
            }

            if (importer.maxTextureSize < 1024)
            {
                importer.maxTextureSize = 1024;
                dirty = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                dirty = true;
            }

            return dirty;
        }

        // -------------------------------------------------------------- report ---

        public static void Report()
        {
            Debug.Log(Describe());
        }

        /// <summary>One line per Android icon slot and the texture (per layer) it carries.</summary>
        public static string Describe()
        {
            var lines = new List<string> { "[AppIcons] Assigned Android icons:" };
            foreach (var name in new[] { "Legacy", "Round", "Adaptive" })
            {
                var kind = GetAndroidKind(name);
                foreach (var icon in PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind))
                {
                    var layers = new List<string>();
                    for (var layer = 0; layer < 2; layer++)
                    {
                        var texture = GetTexture(icon, layer);
                        if (texture != null)
                        {
                            layers.Add($"{(layer == 0 ? "bg" : "fg")}={texture.name} ({texture.width}px)");
                        }
                    }

                    lines.Add($"  {name,-8} {icon.width}x{icon.height}  " +
                              (layers.Count == 0 ? "<none>" : string.Join(" ", layers)));
                }
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Flushes Player Settings to `ProjectSettings/ProjectSettings.asset`. Unity keeps Player
        /// Settings in memory and does not write them for `File/Save Project`, so the settings asset
        /// is dirtied and saved explicitly - otherwise the assignment survives only until the
        /// editor exits, and is lost outright if the editor is killed.
        /// </summary>
        public static void SaveProjectSettings()
        {
            AssetDatabase.SaveAssets();
            var settings = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings == null)
            {
                Debug.LogWarning("[AppIcons] Could not load PlayerSettings asset to flush it.");
                return;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }
    }
}
