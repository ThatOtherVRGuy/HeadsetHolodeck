using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HeadsetHolodeck.Editor
{
    public sealed class HeadsetHolodeckInstallValidator : EditorWindow
    {
        const string ExpectedUnityVersion = "6000.2.10f1";

        readonly List<ValidationItem> _items = new List<ValidationItem>();
        Vector2 _scroll;
        string _projectRoot;
        string _summary = string.Empty;

        [MenuItem("Headset Holodeck/Validate Install")]
        public static void ShowWindow()
        {
            HeadsetHolodeckInstallValidator window = GetWindow<HeadsetHolodeckInstallValidator>("Holodeck Install");
            window.minSize = new Vector2(640f, 520f);
            window.Refresh();
            window.Show();
        }

        void OnEnable()
        {
            Refresh();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Headset Holodeck Install Validator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Checks the files and settings a tester needs after cloning the public repo. Secret values are never printed.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(120f)))
            {
                Refresh();
            }

            if (GUILayout.Button("Create .env From Example", GUILayout.Width(190f)))
            {
                CreateEnvFromExample();
                Refresh();
            }

            if (GUILayout.Button("Open README", GUILayout.Width(120f)))
            {
                OpenPath(Path.Combine(_projectRoot, "README.md"));
            }

            if (GUILayout.Button("Reveal Project", GUILayout.Width(120f)))
            {
                EditorUtility.RevealInFinder(_projectRoot);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(_summary, EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (ValidationItem item in _items)
            {
                DrawItem(item);
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawItem(ValidationItem item)
        {
            MessageType messageType = MessageType.None;
            if (item.Status == ValidationStatus.Warning)
            {
                messageType = MessageType.Warning;
            }
            else if (item.Status == ValidationStatus.Fail)
            {
                messageType = MessageType.Error;
            }

            string label = item.Status.ToString().ToUpperInvariant() + ": " + item.Title;
            if (messageType == MessageType.None)
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(item.Detail))
                {
                    EditorGUILayout.LabelField(item.Detail, EditorStyles.wordWrappedMiniLabel);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(label + "\n" + item.Detail, messageType);
            }

            EditorGUILayout.Space(4f);
        }

        void Refresh()
        {
            _items.Clear();
            _projectRoot = Directory.GetParent(Application.dataPath).FullName;

            Dictionary<string, string> env = ReadDotEnv(Path.Combine(_projectRoot, ".env"));

            CheckUnityVersion();
            CheckFile("Main scene", "Assets/Scenes/Holodeck.unity", true);
            CheckFile("Project README", "README.md", true);
            CheckFile("Environment example", ".env.example", true);
            CheckPackage("World Labs Gaussian Splatting package", "Packages/com.worldlabs.gaussian-splatting/package.json");
            CheckPackage("Sherpa-ONNX Unity package", "Packages/com.ponyudev.sherpa-onnx/package.json");
            CheckPackageManifest();
            CheckSherpaFiles();
            CheckAndroidFiles();
            CheckEnv(env);
            CheckOptionalEditorUtilities();

            int failures = 0;
            int warnings = 0;
            foreach (ValidationItem item in _items)
            {
                if (item.Status == ValidationStatus.Fail)
                {
                    failures++;
                }
                else if (item.Status == ValidationStatus.Warning)
                {
                    warnings++;
                }
            }

            _summary = string.Format(
                "{0} check(s): {1} failure(s), {2} warning(s)",
                _items.Count,
                failures,
                warnings);

            LogReport(failures, warnings);
        }

        void CheckUnityVersion()
        {
            string version = Application.unityVersion;
            if (version == ExpectedUnityVersion)
            {
                AddOk("Unity version", version);
                return;
            }

            ValidationStatus status = version.StartsWith("6000.2.", StringComparison.Ordinal)
                ? ValidationStatus.Warning
                : ValidationStatus.Fail;

            Add(
                status,
                "Unity version",
                "Expected " + ExpectedUnityVersion + ". Current editor is " + version + ".");
        }

        void CheckPackage(string title, string relativePackageJson)
        {
            CheckFile(title, relativePackageJson, true);
        }

        void CheckPackageManifest()
        {
            string path = FullPath("Packages/manifest.json");
            if (!File.Exists(path))
            {
                AddFail("Package manifest", "Packages/manifest.json is missing.");
                return;
            }

            string manifest = File.ReadAllText(path);
            bool hasWorldLabs = manifest.Contains("\"com.worldlabs.gaussian-splatting\": \"file:com.worldlabs.gaussian-splatting\"");
            bool hasSherpaPackage = Directory.Exists(FullPath("Packages/com.ponyudev.sherpa-onnx"));

            if (hasWorldLabs && hasSherpaPackage)
            {
                AddOk("Package manifest", "Vendored package dependencies are present.");
                return;
            }

            AddFail(
                "Package manifest",
                "Expected vendored package references/folders for World Labs and Sherpa-ONNX.");
        }

        void CheckSherpaFiles()
        {
            string[] requiredFiles =
            {
                "Assets/StreamingAssets/SherpaOnnx/asr-settings.json",
                "Assets/StreamingAssets/SherpaOnnx/online-asr-settings.json",
                "Assets/StreamingAssets/SherpaOnnx/vad-settings.json",
                "Assets/StreamingAssets/SherpaOnnx/tts-settings.json",
                "Assets/StreamingAssets/SherpaOnnx/streaming-assets-manifest.json",
                "Assets/StreamingAssets/SherpaOnnx/asr-models/sherpa-onnx-zipformer-small-en-2023-06-26/encoder-epoch-99-avg-1.int8.onnx",
                "Assets/StreamingAssets/SherpaOnnx/asr-models/sherpa-onnx-zipformer-small-en-2023-06-26/decoder-epoch-99-avg-1.int8.onnx",
                "Assets/StreamingAssets/SherpaOnnx/asr-models/sherpa-onnx-zipformer-small-en-2023-06-26/joiner-epoch-99-avg-1.int8.onnx",
                "Assets/StreamingAssets/SherpaOnnx/asr-models/sherpa-onnx-zipformer-small-en-2023-06-26/tokens.txt",
                "Assets/StreamingAssets/SherpaOnnx/vad-models/silero_vad/silero_vad.onnx",
                "Assets/StreamingAssets/SherpaOnnx/tts-models/vits-piper-en_US-kristin-medium-int8/en_US-kristin-medium.onnx",
                "Assets/StreamingAssets/SherpaOnnx/tts-models/vits-piper-en_US-kristin-medium-int8/en_US-kristin-medium.onnx.json",
                "Assets/Plugins/SherpaOnnx/Android/arm64-v8a/libsherpa-onnx-jni.so",
                "Assets/Plugins/SherpaOnnx/Android/arm64-v8a/libsherpa-onnx-c-api.so",
                "Assets/Plugins/SherpaOnnx/Android/arm64-v8a/libonnxruntime.so",
                "Assets/Plugins/SherpaOnnx/osx-arm64/libsherpa-onnx-c-api.dylib",
                "Assets/Plugins/SherpaOnnx/osx-arm64/libonnxruntime.dylib"
            };

            List<string> missing = MissingFiles(requiredFiles);
            if (missing.Count == 0)
            {
                AddOk("Sherpa-ONNX files", "Required ASR, VAD, TTS, and native runtime files are present.");
                return;
            }

            AddFail("Sherpa-ONNX files", "Missing:\n" + string.Join("\n", missing.ToArray()));
        }

        void CheckAndroidFiles()
        {
            string manifestPath = FullPath("Assets/Plugins/Android/AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                AddFail("Android manifest", "Assets/Plugins/Android/AndroidManifest.xml is missing.");
                return;
            }

            string manifest = File.ReadAllText(manifestPath);
            string[] requiredManifestTokens =
            {
                "android.permission.INTERNET",
                "android.permission.CAMERA",
                "android.permission.RECORD_AUDIO",
                "com.oculus.permission.HAND_TRACKING",
                "horizonos.permission.HEADSET_CAMERA",
                "android.hardware.vulkan.version"
            };

            List<string> missingTokens = MissingTokens(manifest, requiredManifestTokens);
            if (missingTokens.Count == 0)
            {
                AddOk("Android manifest", "Quest permissions and Vulkan feature declaration are present.");
            }
            else
            {
                AddFail("Android manifest", "Missing token(s): " + string.Join(", ", missingTokens.ToArray()));
            }

            string gradlePath = FullPath("Assets/Plugins/Android/HeadsetCameraPermissions.androidlib/build.gradle");
            if (!File.Exists(gradlePath))
            {
                AddFail("Headset camera Android library", "build.gradle is missing.");
                return;
            }

            string gradle = File.ReadAllText(gradlePath);
            if (gradle.Contains("namespace ") && gradle.Contains("compileSdk"))
            {
                AddOk("Headset camera Android library", "Namespace and compileSdk are configured.");
            }
            else
            {
                AddFail("Headset camera Android library", "Expected namespace and compileSdk in build.gradle.");
            }
        }

        void CheckEnv(Dictionary<string, string> env)
        {
            string envPath = Path.Combine(_projectRoot, ".env");
            if (File.Exists(envPath))
            {
                AddOk(".env file", ".env exists in the project root.");
            }
            else
            {
                AddWarning(".env file", "No project-root .env found. Copy .env.example to .env and add local keys.");
            }

            CheckEnvKey(env, "OPENAI_API_KEY", true);
            CheckEnvKey(env, "WORLDLABS_API_KEY", true);
            CheckEnvKey(env, "PIXABAY_API_KEY", false);
            CheckEnvKey(env, "FREESOUND_API_KEY", false);
            CheckEnvKey(env, "XENO_CANTO_API_KEY", false);
            CheckEnvKey(env, "MESHY_API_KEY", false);
            CheckEnvKey(env, "TRIPO_API_KEY", false);
            CheckEnvKey(env, "HITEM_API_KEY", false);
        }

        void CheckEnvKey(Dictionary<string, string> env, string key, bool required)
        {
            string value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                env.TryGetValue(key, out value);
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                AddOk(key, "Configured.");
                return;
            }

            if (required)
            {
                AddFail(key, "Missing. Add it to .env or the process environment.");
            }
            else
            {
                AddWarning(key, "Missing. Related optional UI/features may be disabled.");
            }
        }

        void CheckOptionalEditorUtilities()
        {
            string[] setupScripts =
            {
                "Assets/App/Editor/SpeechIntentSceneSetup.cs",
                "Assets/App/Editor/VoiceActivationSceneSetup.cs",
                "Assets/App/Editor/InteractableObjectSetup.cs",
                "Assets/App/Editor/HeadsetHolodeckInstallValidator.cs"
            };

            List<string> missing = MissingFiles(setupScripts);
            if (missing.Count == 0)
            {
                AddOk("Editor utilities", "Validation and repair/setup utilities are present.");
            }
            else
            {
                AddWarning("Editor utilities", "Some optional editor utilities are missing:\n" + string.Join("\n", missing.ToArray()));
            }
        }

        void CheckFile(string title, string relativePath, bool required)
        {
            if (File.Exists(FullPath(relativePath)))
            {
                AddOk(title, relativePath);
                return;
            }

            if (required)
            {
                AddFail(title, relativePath + " is missing.");
            }
            else
            {
                AddWarning(title, relativePath + " is missing.");
            }
        }

        void CreateEnvFromExample()
        {
            string envPath = Path.Combine(_projectRoot, ".env");
            string examplePath = Path.Combine(_projectRoot, ".env.example");

            if (File.Exists(envPath))
            {
                EditorUtility.DisplayDialog(".env already exists", "A project-root .env file already exists. It was not overwritten.", "OK");
                return;
            }

            if (!File.Exists(examplePath))
            {
                EditorUtility.DisplayDialog(".env.example missing", "Could not find .env.example in the project root.", "OK");
                return;
            }

            File.Copy(examplePath, envPath);
            EditorUtility.DisplayDialog(".env created", "Created .env from .env.example. Add your local API keys before running API-backed features.", "OK");
        }

        Dictionary<string, string> ReadDotEnv(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
            {
                return values;
            }

            string[] lines = File.ReadAllLines(path);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, equalsIndex).Trim();
                string value = line.Substring(equalsIndex + 1).Trim().Trim('"');
                values[key] = value;
            }

            return values;
        }

        List<string> MissingFiles(string[] relativePaths)
        {
            List<string> missing = new List<string>();
            foreach (string relativePath in relativePaths)
            {
                if (!File.Exists(FullPath(relativePath)))
                {
                    missing.Add(relativePath);
                }
            }

            return missing;
        }

        List<string> MissingTokens(string text, string[] tokens)
        {
            List<string> missing = new List<string>();
            foreach (string token in tokens)
            {
                if (text.IndexOf(token, StringComparison.Ordinal) < 0)
                {
                    missing.Add(token);
                }
            }

            return missing;
        }

        string FullPath(string relativePath)
        {
            return Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        void OpenPath(string path)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                EditorUtility.OpenWithDefaultApp(path);
            }
        }

        void AddOk(string title, string detail)
        {
            Add(ValidationStatus.Ok, title, detail);
        }

        void AddWarning(string title, string detail)
        {
            Add(ValidationStatus.Warning, title, detail);
        }

        void AddFail(string title, string detail)
        {
            Add(ValidationStatus.Fail, title, detail);
        }

        void Add(ValidationStatus status, string title, string detail)
        {
            _items.Add(new ValidationItem(status, title, detail));
        }

        void LogReport(int failures, int warnings)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[HeadsetHolodeckInstallValidator] " + _summary);
            foreach (ValidationItem item in _items)
            {
                builder.Append(item.Status.ToString().ToUpperInvariant());
                builder.Append(": ");
                builder.Append(item.Title);
                if (!string.IsNullOrEmpty(item.Detail))
                {
                    builder.Append(" - ");
                    builder.Append(item.Detail.Replace('\n', ' '));
                }

                builder.AppendLine();
            }

            if (failures > 0)
            {
                Debug.LogError(builder.ToString());
            }
            else if (warnings > 0)
            {
                Debug.LogWarning(builder.ToString());
            }
            else
            {
                Debug.Log(builder.ToString());
            }
        }

        enum ValidationStatus
        {
            Ok,
            Warning,
            Fail
        }

        readonly struct ValidationItem
        {
            public readonly ValidationStatus Status;
            public readonly string Title;
            public readonly string Detail;

            public ValidationItem(ValidationStatus status, string title, string detail)
            {
                Status = status;
                Title = title;
                Detail = detail;
            }
        }
    }
}
