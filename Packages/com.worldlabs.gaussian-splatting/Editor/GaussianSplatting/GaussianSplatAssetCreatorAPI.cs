// SPDX-License-Identifier: MIT

using System;
using System.IO;
using GaussianSplatting.Editor.Utils;
using GaussianSplatting.Runtime;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace GaussianSplatting.Editor
{
    /// <summary>
    /// Public API for creating GaussianSplatAsset programmatically in the Editor.
    /// The heavy data processing is delegated to <see cref="RuntimeSplatProcessing"/> which
    /// has no Editor dependencies and can also be used at runtime.
    /// </summary>
    public static class GaussianSplatAssetCreatorAPI
    {
        private const string kProgressTitle = "Creating Gaussian Splat Asset";

        /// <summary>
        /// Creates a GaussianSplatAsset from an SPZ or PLY file.
        /// </summary>
        public static GaussianSplatAsset CreateAsset(
            string inputFilePath,
            string outputFolder,
            string baseName,
            GaussianSplatAsset.VectorFormat formatPos,
            GaussianSplatAsset.VectorFormat formatScale,
            GaussianSplatAsset.ColorFormat  formatColor,
            GaussianSplatAsset.SHFormat     formatSH,
            bool importCameras = false)
        {
            if (string.IsNullOrWhiteSpace(inputFilePath))
            {
                Debug.LogError("[GaussianSplatAssetCreatorAPI] Input file path is empty");
                return null;
            }
            if (!File.Exists(inputFilePath))
            {
                Debug.LogError($"[GaussianSplatAssetCreatorAPI] Input file not found: {inputFilePath}");
                return null;
            }
            if (string.IsNullOrWhiteSpace(outputFolder) || !outputFolder.StartsWith("Assets/"))
            {
                Debug.LogError($"[GaussianSplatAssetCreatorAPI] Output folder must be within project Assets/, was '{outputFolder}'");
                return null;
            }

            Directory.CreateDirectory(outputFolder);

            try
            {
                return CreateAssetInternal(inputFilePath, outputFolder, baseName, formatPos, formatScale, formatColor, formatSH, importCameras);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GaussianSplatAssetCreatorAPI] Failed to create asset: {ex.Message}");
                EditorUtility.ClearProgressBar();
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────

        private static GaussianSplatAsset CreateAssetInternal(
            string inputFilePath,
            string outputFolder,
            string baseName,
            GaussianSplatAsset.VectorFormat formatPos,
            GaussianSplatAsset.VectorFormat formatScale,
            GaussianSplatAsset.ColorFormat  formatColor,
            GaussianSplatAsset.SHFormat     formatSH,
            bool importCameras)
        {
            EditorUtility.DisplayProgressBar(kProgressTitle, "Reading data files", 0.0f);

            GaussianSplatAsset.CameraInfo[] cameras = importCameras ? LoadJsonCamerasFile(inputFilePath) : null;

            NativeArray<InputSplatData> inputSplats = default;
            try
            {
                GaussianFileReader.ReadFile(inputFilePath, out inputSplats);
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                throw new Exception($"Failed to read input file: {ex.Message}");
            }

            if (inputSplats.Length == 0)
            {
                EditorUtility.ClearProgressBar();
                throw new Exception("Input file contains no splat data");
            }

            try
            {
                // Delegate all heavy processing to the runtime-safe class.
                RuntimeSplatData runtimeData = RuntimeSplatProcessing.Process(
                    inputSplats, formatPos, formatScale, formatColor, formatSH,
                    (msg, t) => EditorUtility.DisplayProgressBar(kProgressTitle, msg, t * 0.85f));

                EditorUtility.DisplayProgressBar(kProgressTitle, "Writing data files", 0.87f);

                string pathChunk = $"{outputFolder}/{baseName}_chk.bytes";
                string pathPos   = $"{outputFolder}/{baseName}_pos.bytes";
                string pathOther = $"{outputFolder}/{baseName}_oth.bytes";
                string pathCol   = $"{outputFolder}/{baseName}_col.bytes";
                string pathSh    = $"{outputFolder}/{baseName}_shs.bytes";

                bool useChunks = runtimeData.chkData != null;
                if (useChunks) File.WriteAllBytes(pathChunk, runtimeData.chkData);
                File.WriteAllBytes(pathPos,   runtimeData.posData);
                File.WriteAllBytes(pathOther, runtimeData.othData);
                File.WriteAllBytes(pathCol,   runtimeData.colData);
                File.WriteAllBytes(pathSh,    runtimeData.shData);

                var dataHash = new Hash128((uint)inputSplats.Length, (uint)GaussianSplatAsset.kCurrentVersion, 0, 0);

                EditorUtility.DisplayProgressBar(kProgressTitle, "Initial texture import", 0.90f);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUncompressedImport);

                EditorUtility.DisplayProgressBar(kProgressTitle, "Setup data onto asset", 0.95f);

                int2[] layerInfo = new int2[] { new int2(0, inputSplats.Length) };
                GaussianSplatAsset asset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
                asset.Initialize(inputSplats.Length, formatPos, formatScale, formatColor, formatSH,
                    runtimeData.boundsMin, runtimeData.boundsMax, cameras, layerInfo);
                asset.name = baseName;
                asset.SetDataHash(dataHash);

                asset.SetAssetFiles(
                    0,
                    useChunks ? AssetDatabase.LoadAssetAtPath<TextAsset>(pathChunk) : null,
                    AssetDatabase.LoadAssetAtPath<TextAsset>(pathPos),
                    AssetDatabase.LoadAssetAtPath<TextAsset>(pathOther),
                    AssetDatabase.LoadAssetAtPath<TextAsset>(pathCol),
                    AssetDatabase.LoadAssetAtPath<TextAsset>(pathSh));

                string assetPath  = $"{outputFolder}/{baseName}.asset";
                var    savedAsset = CreateOrReplaceAsset(asset, assetPath);

                EditorUtility.DisplayProgressBar(kProgressTitle, "Saving assets", 0.99f);
                AssetDatabase.SaveAssets();

                FixAssetEditorClassIdentifier(assetPath);
                EditorUtility.ClearProgressBar();
                return savedAsset;
            }
            finally
            {
                if (inputSplats.IsCreated) inputSplats.Dispose();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Editor utilities
        // ─────────────────────────────────────────────────────────────────────

        private static T CreateOrReplaceAsset<T>(T asset, string path) where T : UnityEngine.Object
        {
            T result = AssetDatabase.LoadAssetAtPath<T>(path);
            if (result == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                result = asset;
            }
            else
            {
                if (typeof(Mesh).IsAssignableFrom(typeof(T))) { (result as Mesh)?.Clear(); }
                EditorUtility.CopySerialized(asset, result);
            }
            return result;
        }

        private static void FixAssetEditorClassIdentifier(string assetPath)
        {
            try
            {
                string fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath)) return;
                string content = File.ReadAllText(fullPath);
                string pattern = @"m_EditorClassIdentifier: .+";
                string fixed_  = System.Text.RegularExpressions.Regex.Replace(content, pattern, "m_EditorClassIdentifier: ");
                if (fixed_ != content)
                {
                    File.WriteAllText(fullPath, fixed_);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GaussianSplatAssetCreatorAPI] Failed to fix m_EditorClassIdentifier: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Camera loading (editor-only, reads cameras.json)
        // ─────────────────────────────────────────────────────────────────────

        private const string kCamerasJson = "cameras.json";

        private static GaussianSplatAsset.CameraInfo[] LoadJsonCamerasFile(string curPath)
        {
            string camerasPath;
            while (true)
            {
                var dir = Path.GetDirectoryName(curPath);
                if (!Directory.Exists(dir)) return null;
                camerasPath = $"{dir}/{kCamerasJson}";
                if (File.Exists(camerasPath)) break;
                curPath = dir;
            }
            if (!File.Exists(camerasPath)) return null;

            string json        = File.ReadAllText(camerasPath);
            var    jsonCameras = JSONParser.FromJson<System.Collections.Generic.List<JsonCamera>>(json);
            if (jsonCameras == null || jsonCameras.Count == 0) return null;

            var result = new GaussianSplatAsset.CameraInfo[jsonCameras.Count];
            for (int i = 0; i < jsonCameras.Count; i++)
            {
                var jsonCam = jsonCameras[i];
                var pos     = new Vector3(jsonCam.position[0], jsonCam.position[1], jsonCam.position[2]);
                var axisx   = new Vector3(jsonCam.rotation[0][0], jsonCam.rotation[1][0], jsonCam.rotation[2][0]);
                var axisy   = new Vector3(jsonCam.rotation[0][1], jsonCam.rotation[1][1], jsonCam.rotation[2][1]);
                var axisz   = new Vector3(jsonCam.rotation[0][2], jsonCam.rotation[1][2], jsonCam.rotation[2][2]);
                axisy *= -1;
                axisz *= -1;
                result[i] = new GaussianSplatAsset.CameraInfo { pos = pos, axisX = axisx, axisY = axisy, axisZ = axisz, fov = 25 };
            }
            return result;
        }

        [Serializable]
        private class JsonCamera
        {
            public int       id;
            public string    img_name;
            public int       width, height;
            public float[]   position;
            public float[][] rotation;
            public float     fx, fy;
        }
    }
}
