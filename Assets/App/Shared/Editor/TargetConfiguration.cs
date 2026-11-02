using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Holodeck.Editor
{
    /// <summary>
    /// Applies the XR configuration that belongs to the active Unity 6 Build Profile.
    ///
    /// Unity stores OpenXR feature enablement globally (one settings asset shared by every target), and the
    /// Android manifest is generated from that global state. A Build Profile alone therefore cannot isolate
    /// Meta from Pico: this class reads the active profile and flips the feature flags to match, and runs the
    /// same routine before every build so a stale switch cannot leak into an artifact.
    ///
    /// Deliberately not used: per-target <c>#if</c> branches in gameplay code, and hand-editing the merged
    /// Gradle manifest (the Pico SDK injects its own manifest markers once PICOFeature is enabled).
    /// </summary>
    public static class TargetConfiguration
    {
        public const string PicoProfileName = "Pico";

        const string ActiveManifest = "Assets/Plugins/Android/AndroidManifest.xml";
        const string MetaManifestTemplate = "Assets/App/Platforms/Meta/Manifest/AndroidManifest.meta.xml";
        const string PicoManifestTemplate = "Assets/App/Platforms/Pico/Manifest/AndroidManifest.pico.xml";

        /// <summary>Feature names enabled for the Pico target. PICOFeature is what makes the Pico SDK inject its manifest markers.</summary>
        static readonly string[] PicoEnabled =
        {
            "PICOFeature",
            "OpenXRExtensions",
            "PICO4ControllerProfile",
            "PICO4UltraControllerProfile",
            "PICOG3ControllerProfile",
            "PICONeo3ControllerProfile",
        };

        /// <summary>Feature names that must be off for Pico: Meta-only capabilities and Meta controller profiles.</summary>
        static readonly string[] PicoDisabled =
        {
            "MetaQuestFeature",
            "MetaHandTrackingAim",
            "MetaQuestTouchPlusControllerProfile",
            "MetaQuestTouchProControllerProfile",
            "OculusTouchControllerProfile",
        };

        /// <summary>Feature names enabled for the Meta target (the configuration captured in P1-BASELINE.md).</summary>
        static readonly string[] MetaEnabled =
        {
            "MetaQuestFeature",
            "MetaHandTrackingAim",
            "MetaQuestTouchPlusControllerProfile",
            "MetaQuestTouchProControllerProfile",
            "OculusTouchControllerProfile",
        };

        /// <summary>Feature names that must be off for Meta. PICOFeature off also stops Pico manifest markers being injected.</summary>
        static readonly string[] MetaDisabled =
        {
            "PICOFeature",
            "OpenXRExtensions",
            "PICO4ControllerProfile",
            "PICO4UltraControllerProfile",
            "PICOG3ControllerProfile",
            "PICONeo3ControllerProfile",
        };

        public enum Target
        {
            Meta,
            Pico,
        }

        [MenuItem("Headset Holodeck/Target/Apply Meta Configuration", priority = 200)]
        public static void ApplyMeta()
        {
            Apply(Target.Meta, true);
        }

        [MenuItem("Headset Holodeck/Target/Apply Pico Configuration", priority = 201)]
        public static void ApplyPico()
        {
            Apply(Target.Pico, true);
        }

        [MenuItem("Headset Holodeck/Target/Report Active Configuration", priority = 202)]
        public static void Report()
        {
            var active = ActiveProfileName();
            var target = TargetForProfile(active);
            Debug.Log($"[TargetConfiguration] Active Build Profile: {active ?? "(none)"} -> applied target {target}");
            Debug.Log($"[TargetConfiguration] Application identifier (Android): {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}");
            foreach (var line in DescribeEnabled())
            {
                Debug.Log($"[TargetConfiguration]   {line}");
            }
        }

        /// <summary>
        /// Apply the configuration for a target. <paramref name="save"/> persists the settings asset.
        /// Returns false when the target's required features are absent from the settings, because a
        /// half-applied target builds but misbehaves: enabling PICO's loader name without PICOFeature
        /// produces an APK containing both OpenXR loaders, and Gradle then fails on a duplicate
        /// libopenxr_loader.so long after the real cause.
        /// </summary>
        public static bool Apply(Target target, bool save)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
            {
                Debug.LogError("[TargetConfiguration] No Android OpenXR settings found; cannot apply target configuration.");
                return false;
            }

            var enable = target == Target.Pico ? PicoEnabled : MetaEnabled;
            var disable = target == Target.Pico ? PicoDisabled : MetaDisabled;

            var changed = new List<string>();
            var missing = new List<string>();
            var features = settings.GetFeatures<OpenXRFeature>().Where(f => f != null).ToArray();

            foreach (var name in enable.Concat(disable))
            {
                var feature = features.FirstOrDefault(f => f.GetType().Name == name);
                if (feature == null)
                {
                    missing.Add(name);
                    continue;
                }

                var wanted = enable.Contains(name);
                if (feature.enabled == wanted)
                {
                    continue;
                }

                try
                {
                    feature.enabled = wanted;
                    EditorUtility.SetDirty(feature);
                    changed.Add($"{name} -> {wanted}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TargetConfiguration] Could not set {name} to {wanted}: {e.Message}");
                }
            }

            // PICO's Android tooling rejects Unity 6's GameActivity entry point at build time
            // ("Please use Activity instead of GameActivity!"), while the Meta baseline builds with it.
            // This is a second, less obvious Meta/Pico conflict that a Build Profile alone does not isolate.
            var desiredEntry = target == Target.Pico ? AndroidApplicationEntry.Activity : AndroidApplicationEntry.GameActivity;
            if (PlayerSettings.Android.applicationEntry != desiredEntry)
            {
                PlayerSettings.Android.applicationEntry = desiredEntry;
                changed.Add($"applicationEntry -> {desiredEntry}");
            }

            // Enabling PICOFeature makes Unity record the Pico runtime loader name in the shared OpenXR
            // settings. Left behind, a later Meta build would look for that loader and fail, so the name
            // belongs to the switch, not to whichever target last triggered it.
            var desiredLoader = target == Target.Pico ? "LoaderForUnitySDK_1_1_0" : string.Empty;
            var settingsObject = new SerializedObject(settings);
            var loaderProperty = settingsObject.FindProperty("customLoaderName");
            if (loaderProperty != null && loaderProperty.stringValue != desiredLoader)
            {
                loaderProperty.stringValue = desiredLoader;
                settingsObject.ApplyModifiedPropertiesWithoutUndo();
                changed.Add($"customLoaderName -> '{desiredLoader}'");
            }

            // The entry point and the custom main manifest must agree: the Meta manifest names
            // UnityPlayerGameActivity and theme BaseUnityGameActivityTheme, neither of which exists in
            // Activity mode, so Gradle fails with "Resource Not Found ... BaseUnityGameActivityTheme".
            SyncManifest(target, changed);

            if (save)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[TargetConfiguration] Applied {target}: {changed.Count} feature change(s)" +
                      (changed.Count > 0 ? " [" + string.Join(", ", changed) + "]" : string.Empty) +
                      (missing.Count > 0 ? $"; not present in this SDK: {string.Join(", ", missing)}" : string.Empty));

            var appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            var expected = target == Target.Pico ? ".HeadsetHolodeckPico" : ".HeadsetHolodeck";
            if (!appId.EndsWith(expected, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[TargetConfiguration] Application identifier '{appId}' does not match the " +
                                 $"{target} profile (expected it to end with '{expected}'). Check the active Build Profile's player settings override.");
            }

            // Features are toggled, never created: GetFeatures only returns instances already present in the
            // OpenXR settings asset. If this target's features are absent, stop rather than building a hybrid.
            var requiredMissing = enable.Where(n => missing.Contains(n)).ToList();
            if (requiredMissing.Count > 0)
            {
                Debug.LogError(
                    $"[TargetConfiguration] {target} needs {string.Join(", ", requiredMissing)}, but the Android " +
                    "OpenXR settings do not contain them. Toggling features cannot create a missing one: enable " +
                    "the PICO feature group once in Project Settings > XR Plug-in Management > OpenXR > " +
                    "Feature Groups. Building now would half-apply the target and fail later in Gradle with a " +
                    "duplicate libopenxr_loader.so.");
                return false;
            }

            return true;
        }

        /// <summary>Human-readable enabled/disabled state of the features this class manages.</summary>
        public static IEnumerable<string> DescribeEnabled()        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
            {
                yield break;
            }

            foreach (var feature in settings.GetFeatures<OpenXRFeature>().Where(f => f != null).OrderBy(f => f.GetType().Name))
            {
                var name = feature.GetType().Name;
                if (PicoEnabled.Concat(PicoDisabled).Concat(MetaEnabled).Concat(MetaDisabled).Contains(name))
                {
                    yield return $"{name}: {(feature.enabled ? "enabled" : "disabled")}";
                }
            }
        }

        /// <summary>
        /// Copy the target's manifest template over the active main manifest. Unity reads exactly one
        /// <c>Assets/Plugins/Android/AndroidManifest.xml</c>; keeping the two variants as templates and
        /// syncing on switch makes the choice explicit and reversible instead of hand-edited.
        /// </summary>
        static void SyncManifest(Target target, List<string> changed)
        {
            var template = target == Target.Pico ? PicoManifestTemplate : MetaManifestTemplate;
            if (!File.Exists(template))
            {
                Debug.LogWarning($"[TargetConfiguration] Manifest template missing: {template}");
                return;
            }

            var incoming = File.ReadAllText(template).Replace("\r\n", "\n");
            var current = File.Exists(ActiveManifest) ? File.ReadAllText(ActiveManifest).Replace("\r\n", "\n") : null;
            if (current == incoming)
            {
                return;
            }

            File.WriteAllText(ActiveManifest, incoming);
            AssetDatabase.ImportAsset(ActiveManifest);
            changed.Add($"AndroidManifest.xml <- {Path.GetFileName(template)}");
        }

        /// <summary>Compact one-line state of the managed features, for logs, batch checks and evidence capture.</summary>
        public static string StatusLine()        {
            return string.Join("; ", DescribeEnabled().Select(s => s.Replace(": ", "=")));
        }

        /// <summary>Name of the active Build Profile, or null when building on global settings.</summary>
        public static string ActiveProfileName()
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            return profile != null ? profile.name : null;
        }

        static Target TargetForProfile(string profileName)
        {
            return !string.IsNullOrEmpty(profileName) &&
                   profileName.IndexOf(PicoProfileName, StringComparison.OrdinalIgnoreCase) >= 0
                ? Target.Pico
                : Target.Meta;
        }

        /// <summary>
        /// Enforces the active profile's configuration before a build, so a forgotten menu switch cannot produce
        /// a Meta build with Pico markers (or vice versa).
        /// </summary>
        public class BuildPreprocessor : IPreprocessBuildWithReport
        {
            public int callbackOrder => -100;

            public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
            {
                var name = ActiveProfileName();
                var target = TargetForProfile(name);
                Debug.Log($"[TargetConfiguration] Pre-build: active profile '{name ?? "(none)"}' -> {target}");

                if (!Apply(target, false))
                    throw new BuildFailedException(
                        $"[TargetConfiguration] {target} configuration could not be applied; see the error above. " +
                        "Fix the OpenXR feature set before building.");
            }
        }
    }
}
