using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WorldLabs.Editor;

namespace Holodeck.BuildCredentials.Tests
{
    /// <summary>
    /// Work order 04 fixtures for the build credential boundary.
    ///
    /// Every fixture writes its own throwaway .env under the process temp path with obviously fake
    /// values, so no real secret is ever read, written, or logged, and the real project .env and the
    /// real Assets/Resources/WorldLabs are never touched. <see cref="Fixture"/> refuses paths outside
    /// its own temp root.
    /// </summary>
    public class BuildEnvCredentialsTests
    {
        sealed class Fixture : IDisposable
        {
            public readonly string Root;
            public readonly string EnvPath;
            public readonly string EmbeddedPath;

            public Fixture()
            {
                Root = Path.Combine(Path.GetTempPath(), "HolodeckBuildCredentials_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(Root, "resources"));
                EnvPath = Path.Combine(Root, ".env");
                EmbeddedPath = Path.Combine(Root, "resources", BuildEnvCredentials.EmbeddedFileName);
            }

            public void WriteEnv(string content) => File.WriteAllText(EnvPath, content);

            public void WriteEmbedded(string content)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EmbeddedPath));
                File.WriteAllText(EmbeddedPath, content);
            }

            public string ReadEmbedded() => File.ReadAllText(EmbeddedPath);

            public bool EmbeddedExists => File.Exists(EmbeddedPath);

            public string[] TemporaryFiles =>
                Directory.Exists(Path.GetDirectoryName(EmbeddedPath))
                    ? Directory.GetFiles(Path.GetDirectoryName(EmbeddedPath), "*.tmp", SearchOption.AllDirectories)
                    : Array.Empty<string>();

            /// <summary>Guards every path handed to the policy: fixtures stay inside their temp root.</summary>
            public void AssertInsideRoot(string path)
            {
                Assert.IsTrue(Path.GetFullPath(path).StartsWith(Path.GetFullPath(Root), StringComparison.Ordinal),
                    $"Fixture path escaped its temp root: {path}");
            }

            public void Dispose()
            {
                try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
                catch (Exception) { }
            }
        }

        // Obviously fake values; none of these strings may ever reach an embedded file.
        const string OpenAiFake = "fake-openai-value-not-a-real-key";
        const string WorldLabsFake = "fake-worldlabs-value-not-a-real-key";
        const string HitemSecretFake = "fake-hitem-secret-value";
        const string MeshyFake = "fake-meshy-value";
        const string UnrelatedFake = "fake-personal-laptop-value";

        const string DeveloperEnv =
            "# developer machine, never share\n" +
            "\n" +
            "OPENAI_API_KEY=" + OpenAiFake + "\n" +
            "WORLDLABS_API_KEY=" + WorldLabsFake + "\n" +
            "HITEM_SECRET_KEY=" + HitemSecretFake + "\n" +
            "MESHY_API_KEY=" + MeshyFake + "\n" +
            "MY_PERSONAL_LAPTOP=" + UnrelatedFake + "\n" +
            "AWS_PROFILE=personal-account\n" +
            "THREEDAISTUDIO_BASE_URL=https://api.3daistudio.com\n" +
            "HITEM_BASE_URL=\"https://api.hitem3d.ai\"\n";

        [Test]
        public void Resolve_PrefersCommandLineThenEnvironmentThenSavedChoice()
        {
            Assert.AreEqual(BuildCredentialMode.Private,
                BuildEnvCredentials.Resolve("private", "distribution", "distribution"));
            Assert.AreEqual(BuildCredentialMode.Distribution,
                BuildEnvCredentials.Resolve(null, "distribution", "private"));
            Assert.AreEqual(BuildCredentialMode.Private,
                BuildEnvCredentials.Resolve(null, null, "private"));
        }

        [Test]
        public void Resolve_WithNoDeclaration_IsUnspecified()
        {
            Assert.AreEqual(BuildCredentialMode.Unspecified, BuildEnvCredentials.Resolve(null, null, null));
            Assert.AreEqual(BuildCredentialMode.Unspecified, BuildEnvCredentials.Resolve("", "  ", ""));
        }

        [Test]
        public void ParseMode_RejectsAnUnknownValueInsteadOfGuessing()
        {
            var error = Assert.Throws<BuildCredentialModeException>(() => BuildEnvCredentials.ParseMode("shared-ish"));
            StringAssert.Contains(BuildEnvCredentials.ModeEnvironmentVariable, error.Message);
            StringAssert.Contains(BuildEnvCredentials.ModeArgument, error.Message);
        }

        [Test]
        public void ReadCommandLineValue_FindsTheDeclaredArgument()
        {
            string[] args = { "-batchmode", "-credentialMode", "distribution", "-outputPath", "out.apk" };
            Assert.AreEqual("distribution", BuildEnvCredentials.ReadCommandLineValue(args));
            Assert.IsNull(BuildEnvCredentials.ReadCommandLineValue(new[] { "-batchmode" }));
        }

        [Test]
        public void AllowList_NeverNamesACredentialShapedKey()
        {
            foreach (string key in BuildEnvCredentials.DistributionSafeKeys)
            {
                Assert.IsFalse(BuildEnvCredentials.IsCredentialShaped(key),
                    $"Allowlisted key '{key}' looks like a credential.");
                Assert.IsTrue(BuildEnvCredentials.IsDistributionSafe(key));
            }
        }

        [Test]
        public void CredentialShapedKeys_AreRecognised()
        {
            foreach (string key in new[]
                     {
                         "OPENAI_API_KEY", "WORLDLABS_API_KEY", "PIXABAY_API_KEY", "FREESOUND_API_KEY",
                         "XENO_CANTO_API_KEY", "MESHY_API_KEY", "TRIPO_API_KEY", "HITEM_ACCESS_KEY",
                         "HITEM_SECRET_KEY", "SOME_TOKEN", "MY_PASSWORD", "SERVICE_CREDENTIAL",
                     })
            {
                Assert.IsTrue(BuildEnvCredentials.IsCredentialShaped(key), key);
                Assert.IsFalse(BuildEnvCredentials.IsDistributionSafe(key), key);
            }

            Assert.IsFalse(BuildEnvCredentials.IsCredentialShaped("THREEDAISTUDIO_BASE_URL"));
        }

        [Test]
        public void Private_EmbedsEveryEntryUnchanged()
        {
            using var fixture = new Fixture();
            fixture.AssertInsideRoot(fixture.EnvPath);
            fixture.WriteEnv(DeveloperEnv);

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Private);

            Assert.IsTrue(result.WroteEmbeddedFile);
            Assert.AreEqual(0, result.ExcludedKeys.Count);
            string embedded = fixture.ReadEmbedded();
            StringAssert.Contains(OpenAiFake, embedded);
            StringAssert.Contains(WorldLabsFake, embedded);
            StringAssert.Contains("MY_PERSONAL_LAPTOP=" + UnrelatedFake, embedded);
            Assert.AreEqual(8, result.IncludedKeys.Count);
        }

        [Test]
        public void Private_WithNoEnvFile_EmbedsNothingAndCreatesNothing()
        {
            using var fixture = new Fixture();

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Private);

            Assert.IsFalse(result.WroteEmbeddedFile);
            Assert.IsFalse(fixture.EmbeddedExists);
            StringAssert.Contains("no .env", result.Reason);
        }

        [Test]
        public void Distribution_ExcludesCredentialsAndUnrelatedEntries()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv(DeveloperEnv);

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);

            Assert.IsTrue(result.WroteEmbeddedFile);
            CollectionAssert.Contains(result.IncludedKeys, "THREEDAISTUDIO_BASE_URL");
            CollectionAssert.Contains(result.IncludedKeys, "HITEM_BASE_URL");
            foreach (string excluded in new[]
                     {
                         "OPENAI_API_KEY", "WORLDLABS_API_KEY", "HITEM_SECRET_KEY", "MESHY_API_KEY",
                         "MY_PERSONAL_LAPTOP", "AWS_PROFILE",
                     })
            {
                CollectionAssert.Contains(result.ExcludedKeys, excluded);
                Assert.IsFalse(result.IncludedKeys.Contains(excluded), excluded);
            }
        }

        [Test]
        public void Distribution_NeverWritesAValueBelongingToAnExcludedEntry()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv(DeveloperEnv);

            BuildEnvCredentials.Embed(fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);
            string embedded = fixture.ReadEmbedded();

            foreach (string secret in new[] { OpenAiFake, WorldLabsFake, HitemSecretFake, MeshyFake, UnrelatedFake })
                StringAssert.DoesNotContain(secret, embedded);

            StringAssert.DoesNotContain("personal-account", embedded);
            StringAssert.Contains("https://api.3daistudio.com", embedded);
        }

        [Test]
        public void Distribution_DropsAStaleCopyLeftByACrashedPrivateBuild()
        {
            using var fixture = new Fixture();
            fixture.WriteEmbedded("# stale private build\nOPENAI_API_KEY=" + OpenAiFake + "\n");
            fixture.WriteEnv(DeveloperEnv);

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);

            Assert.IsTrue(result.RemovedStaleEmbeddedFile);
            StringAssert.DoesNotContain(OpenAiFake, fixture.ReadEmbedded());
        }

        [Test]
        public void Distribution_RemovesAStaleCopyEvenWhenNothingIsEmbeddable()
        {
            using var fixture = new Fixture();
            fixture.WriteEmbedded("OPENAI_API_KEY=" + OpenAiFake + "\n");
            fixture.WriteEnv("OPENAI_API_KEY=" + OpenAiFake + "\n");

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);

            Assert.IsFalse(result.WroteEmbeddedFile);
            Assert.IsFalse(fixture.EmbeddedExists, "A stale embedded .env survived a distribution build.");
            CollectionAssert.Contains(result.ExcludedKeys, "OPENAI_API_KEY");
        }

        [Test]
        public void Distribution_WithNoEnvFile_WritesNothing()
        {
            using var fixture = new Fixture();

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);

            Assert.IsFalse(result.WroteEmbeddedFile);
            Assert.IsFalse(fixture.EmbeddedExists);
        }

        [Test]
        public void Undeclared_WithCredentials_StopsTheBuildWithActionableText()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv(DeveloperEnv);

            var error = Assert.Throws<BuildCredentialModeException>(() => BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Unspecified));

            StringAssert.Contains(BuildEnvCredentials.PrivateValue, error.Message);
            StringAssert.Contains(BuildEnvCredentials.DistributionValue, error.Message);
            StringAssert.Contains("Headset Holodeck > Build Credentials", error.Message);
            Assert.IsFalse(fixture.EmbeddedExists, "The stopped build must not leave an embedded .env behind.");
        }

        [Test]
        public void Undeclared_WithOnlyNonCredentialEntries_DoesNotStopTheBuild()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv("THREEDAISTUDIO_BASE_URL=https://api.3daistudio.com\nMY_PERSONAL_LAPTOP=dev-box\n");

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Unspecified);

            Assert.IsFalse(result.WroteEmbeddedFile);
            Assert.IsFalse(fixture.EmbeddedExists);
        }

        [Test]
        public void Undeclared_WithNoEnvFile_DoesNotStopTheBuild()
        {
            using var fixture = new Fixture();

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Unspecified);

            Assert.IsFalse(result.WroteEmbeddedFile);
            Assert.IsFalse(fixture.EmbeddedExists);
        }

        [Test]
        public void Undeclared_WithUnfilledCredentialSlots_DoesNotStopTheBuild()
        {
            // The shipped .env.example has empty credential slots; a developer who has not filled
            // them in yet must not be blocked from building.
            using var fixture = new Fixture();
            fixture.WriteEnv("OPENAI_API_KEY=\nWORLDLABS_API_KEY=\nTHREEDAISTUDIO_BASE_URL=\n");

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Unspecified);

            Assert.IsFalse(result.WroteEmbeddedFile);
            Assert.IsFalse(fixture.EmbeddedExists);
        }

        [Test]
        public void EmbeddedDocument_KeepsTheRuntimeEnvFormat()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv(DeveloperEnv);

            BuildEnvCredentials.Embed(fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);
            List<BuildEnvCredentials.Entry> entries = BuildEnvCredentials.Parse(fixture.ReadEmbedded());

            var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (BuildEnvCredentials.Entry entry in entries) byKey[entry.Key] = entry.Value;

            Assert.AreEqual(2, byKey.Count);
            Assert.AreEqual("https://api.3daistudio.com", byKey["THREEDAISTUDIO_BASE_URL"]);
            Assert.AreEqual("https://api.hitem3d.ai", byKey["HITEM_BASE_URL"], "Surrounding quotes must still be stripped on read.");
            Assert.IsTrue(fixture.ReadEmbedded().StartsWith("#"), "The generated document should explain itself.");
        }

        [Test]
        public void Embed_LeavesNoTemporaryFileBehind()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv(DeveloperEnv);

            BuildEnvCredentials.Embed(fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);

            Assert.IsEmpty(fixture.TemporaryFiles);
        }

        [Test]
        public void Embed_ReplacesAnExistingDocumentWithoutLeavingOneBehind()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv("THREEDAISTUDIO_BASE_URL=https://first.example\n");
            BuildEnvCredentials.Embed(fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);
            fixture.WriteEnv("HITEM_BASE_URL=https://second.example\n");
            BuildEnvCredentials.Embed(fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Distribution);

            string embedded = fixture.ReadEmbedded();
            StringAssert.Contains("second.example", embedded);
            StringAssert.DoesNotContain("first.example", embedded);
            Assert.IsEmpty(fixture.TemporaryFiles);
        }

        [Test]
        public void RemoveEmbedded_DeletesTheCopyAndItsMetadata()
        {
            using var fixture = new Fixture();
            fixture.WriteEmbedded("OPENAI_API_KEY=" + OpenAiFake + "\n");
            File.WriteAllText(fixture.EmbeddedPath + ".meta", "fileFormatVersion: 2\n");

            Assert.IsTrue(BuildEnvCredentials.RemoveEmbedded(fixture.EmbeddedPath));
            Assert.IsFalse(File.Exists(fixture.EmbeddedPath));
            Assert.IsFalse(File.Exists(fixture.EmbeddedPath + ".meta"));
            Assert.IsFalse(BuildEnvCredentials.RemoveEmbedded(fixture.EmbeddedPath), "Second removal should report nothing to do.");
        }

        [Test]
        public void DeviceMode_EmbedsNothingEvenWhenTheEnvHasCredentials()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv(DeveloperEnv);

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Device);

            Assert.IsFalse(result.WroteEmbeddedFile, "A device build must not write an embedded copy.");
            Assert.IsFalse(fixture.EmbeddedExists, "No embedded document may exist after a device build.");
            Assert.IsEmpty(result.IncludedKeys, "A device build includes no entries at all.");
            Assert.IsEmpty(fixture.TemporaryFiles);
            // Only the build's own resources directory matters here: the fixture's source .env of
            // course still holds the fake values, but nothing may have been copied out of it.
            string resourcesDir = Path.GetDirectoryName(fixture.EmbeddedPath);
            foreach (string secret in new[] { OpenAiFake, WorldLabsFake, HitemSecretFake, MeshyFake })
            {
                Assert.IsFalse(DirectoryContains(resourcesDir, secret),
                    "No credential value may appear anywhere in a device build's resources.");
            }
        }

        [Test]
        public void DeviceMode_RemovesAStaleCopyLeftByAnEarlierBuild()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv(DeveloperEnv);
            fixture.WriteEmbedded("OPENAI_API_KEY=" + OpenAiFake + "\n");

            BuildEnvEmbedResult result = BuildEnvCredentials.Embed(
                fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Device);

            Assert.IsTrue(result.RemovedStaleEmbeddedFile, "A stale copy from a private build must be removed.");
            Assert.IsFalse(fixture.EmbeddedExists);
            foreach (string key in new[] { "OPENAI_API_KEY", "WORLDLABS_API_KEY", "MY_PERSONAL_LAPTOP" })
            {
                Assert.Contains(key, result.ExcludedKeys);
            }
        }

        [Test]
        public void DeviceMode_CanBeDeclaredOnTheCommandLineOrEnvironment()
        {
            Assert.AreEqual(BuildCredentialMode.Device, BuildEnvCredentials.ParseMode("device"));
            Assert.AreEqual(BuildCredentialMode.Device, BuildEnvCredentials.ParseMode("  DEVICE  "));
            Assert.AreEqual(BuildCredentialMode.Device,
                BuildEnvCredentials.Resolve(BuildEnvCredentials.DeviceValue, "private", "distribution"),
                "The command line must win over the environment and the saved choice.");
            Assert.AreEqual(BuildCredentialMode.Device,
                BuildEnvCredentials.Resolve(null, BuildEnvCredentials.DeviceValue, "private"));
            Assert.AreEqual(BuildCredentialMode.Device,
                BuildEnvCredentials.Resolve(null, null, BuildEnvCredentials.DeviceValue));
            Assert.AreEqual("Device build (embeds nothing; the app reads .env from the device)",
                BuildCredentialSettings.DescribeValue(BuildCredentialMode.Device));
        }

        [Test]
        public void UnspecifiedMode_TellsYouAboutTheDeviceOption()
        {
            using var fixture = new Fixture();
            fixture.WriteEnv(DeveloperEnv);

            var error = Assert.Throws<BuildCredentialModeException>(() =>
                BuildEnvCredentials.Embed(fixture.EnvPath, fixture.EmbeddedPath, BuildCredentialMode.Unspecified));

            StringAssert.Contains(BuildEnvCredentials.PrivateValue, error.Message);
            StringAssert.Contains(BuildEnvCredentials.DistributionValue, error.Message);
            StringAssert.Contains(BuildEnvCredentials.DeviceValue, error.Message);
        }

        [Test]
        public void EnvLoader_PrefersTheDeviceFileOverAnyEmbeddedCopy()
        {
            Type envLoader = FindType("WorldLabs.API.EnvLoader");
            Assert.IsNotNull(envLoader, "The World Labs EnvLoader should be present in the project." );

            var describe = envLoader.GetMethod("DescribeSourceOrder", new[] { typeof(bool) });
            Assert.IsNotNull(describe, "EnvLoader should expose its source order for verification.");

            var android = (string[])describe.Invoke(null, new object[] { true });
            Assert.AreEqual(3, android.Length, "On Android there is no project-root .env: " + string.Join(" -> ", android));
            StringAssert.Contains(".env", android[0]);
            Assert.AreEqual("embedded resources", android[1], "A baked copy must never come before the device file: "
                + string.Join(" -> ", android));
            Assert.IsFalse(string.Join(" -> ", android).Contains("project-root"),
                "Android must not look for a project-root .env: " + string.Join(" -> ", android));

            var desktop = (string[])describe.Invoke(null, new object[] { false });
            Assert.AreEqual("project-root .env", desktop[1], "Desktop keeps the project-root convenience path.");
            Assert.AreEqual("embedded resources", desktop[2], "The baked copy stays last on desktop too.");

            var devicePathProperty = envLoader.GetProperty("DeviceEnvPath");
            Assert.IsNotNull(devicePathProperty, "EnvLoader should say where the device .env lives.");
            string devicePath = (string)devicePathProperty.GetValue(null);
            Assert.IsTrue(devicePath.EndsWith(".env", StringComparison.Ordinal), devicePath);

            Type runtimeDotEnv = FindType("SpeechIntent.RuntimeDotEnv");
            Assert.IsNotNull(runtimeDotEnv, "RuntimeDotEnv should be present in the project.");
            var expectedPath = (string)runtimeDotEnv.GetProperty("ExpectedPersistentPath").GetValue(null);
            Assert.AreEqual(Path.GetFullPath(expectedPath), Path.GetFullPath(devicePath),
                "The app and the World Labs plugin must read the same device file.");
        }

        [Test]
        public void RuntimeDotEnv_LoadsBeforeTheFirstScene()
        {
            Type runtimeDotEnv = FindType("SpeechIntent.RuntimeDotEnv");
            Assert.IsNotNull(runtimeDotEnv, "RuntimeDotEnv should be present in the project.");

            bool found = false;
            foreach (System.Reflection.MethodInfo method in runtimeDotEnv.GetMethods(
                         System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Public))
            {
                foreach (object attribute in method.GetCustomAttributes(true))
                {
                    if (attribute is UnityEngine.RuntimeInitializeOnLoadMethodAttribute init)
                    {
                        found = true;
                        Assert.AreEqual(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad, init.loadType,
                            "The device .env must be loaded before scene components ask for keys.");
                    }
                }
            }

            Assert.IsTrue(found, "RuntimeDotEnv needs a [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] entry point.");
        }

        /// <summary>True when any file under <paramref name="root"/> contains <paramref name="needle"/>.</summary>
        static bool DirectoryContains(string root, string needle)
        {
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (File.ReadAllText(file).Contains(needle, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        static Type FindType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch (Exception)
                {
                    // A dynamic or partially loaded assembly is not interesting here.
                }
            }
            return null;
        }

        [Test]
        public void EmbeddedPath_MatchesTheRuntimeResourcesName()
        {
            Assert.AreEqual("worldlabs_env.txt", BuildEnvCredentials.EmbeddedFileName);
            Assert.AreEqual("WorldLabs/worldlabs_env",
                "WorldLabs/" + Path.GetFileNameWithoutExtension(BuildEnvCredentials.EmbeddedFileName));
        }
    }
}
