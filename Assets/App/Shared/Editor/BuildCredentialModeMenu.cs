using UnityEditor;
using UnityEngine;
using WorldLabs.Editor;

namespace Holodeck.Editor
{
    /// <summary>
    /// Declares whether the next build is a private developer build (may embed the developer's .env),
    /// a distribution build (non-secret configuration only), or a device build (embeds nothing; the
    /// app reads .env from the target's own persistent data path). The choice is remembered per
    /// machine and per project in EditorPrefs, so it is never committed and never shipped.
    ///
    /// Order 04 policy: a build with no declared intent refuses to embed credentials rather than
    /// guessing, so a shared artifact can never leak keys by accident.
    /// </summary>
    internal static class BuildCredentialModeMenu
    {
        const string Root = "Headset Holodeck/Build Credentials/";
        const string PrivateItem = Root + "Private (this machine)";
        const string DistributionItem = Root + "Distribution (shareable)";
        const string DeviceItem = Root + "Device (reads .env on the headset)";
        const string ClearItem = Root + "Clear Choice";
        const string StatusItem = Root + "Show Build Credential Status";

        [MenuItem(PrivateItem, false, 100)]
        static void SelectPrivate() => Select(BuildCredentialMode.Private);

        [MenuItem(DistributionItem, false, 101)]
        static void SelectDistribution() => Select(BuildCredentialMode.Distribution);

        [MenuItem(DeviceItem, false, 102)]
        static void SelectDevice() => Select(BuildCredentialMode.Device);

        [MenuItem(ClearItem, false, 103)]
        static void ClearChoice()
        {
            BuildCredentialSettings.Clear();
            Debug.Log("[BuildCredentials] Cleared the saved build credential choice. " +
                      "The next build with a credential-bearing .env will stop until you choose.");
        }

        [MenuItem(StatusItem, false, 120)]
        static void ShowStatus()
        {
            Debug.Log($"[BuildCredentials] Current intent: {BuildCredentialSettings.Describe()}");
        }

        [MenuItem(PrivateItem, true)]
        static bool ValidatePrivate()
        {
            Menu.SetChecked(PrivateItem, BuildCredentialSettings.SavedValue == BuildEnvCredentials.PrivateValue);
            return true;
        }

        [MenuItem(DistributionItem, true)]
        static bool ValidateDistribution()
        {
            Menu.SetChecked(DistributionItem, BuildCredentialSettings.SavedValue == BuildEnvCredentials.DistributionValue);
            return true;
        }

        [MenuItem(DeviceItem, true)]
        static bool ValidateDevice()
        {
            Menu.SetChecked(DeviceItem, BuildCredentialSettings.SavedValue == BuildEnvCredentials.DeviceValue);
            return true;
        }

        [MenuItem(ClearItem, true)]
        static bool ValidateClear()
        {
            Menu.SetChecked(ClearItem, string.IsNullOrEmpty(BuildCredentialSettings.SavedValue));
            return true;
        }

        static void Select(BuildCredentialMode mode)
        {
            BuildCredentialSettings.Save(mode);
            Debug.Log($"[BuildCredentials] Saved build credential intent: {BuildCredentialSettings.Describe()}");
        }
    }
}
