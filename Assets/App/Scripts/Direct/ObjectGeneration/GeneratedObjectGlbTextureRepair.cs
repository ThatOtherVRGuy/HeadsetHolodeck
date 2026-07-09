using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Holodeck.Direct
{
    public static class GeneratedObjectGlbTextureRepair
    {
        const uint GlbMagic = 0x46546C67;
        const uint JsonChunkType = 0x4E4F534A;
        const uint BinChunkType = 0x004E4942;

        public static Texture2D ExtractFirstBaseColorTexture(byte[] glbBytes)
        {
            if (!TryReadFirstBaseColorImage(glbBytes, out byte[] imageBytes, out string imageName))
                return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true)
            {
                name = string.IsNullOrWhiteSpace(imageName) ? "GeneratedObject_BaseColorTexture" : imageName
            };

            if (!texture.LoadImage(imageBytes, markNonReadable: false))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return texture;
        }

        public static bool RepairMissingBaseColorTextures(GameObject root, byte[] glbBytes, Color fallbackColor)
        {
            if (root == null || glbBytes == null || glbBytes.Length == 0)
                return false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            Texture2D baseColorTexture = null;
            bool changed = false;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                bool rendererChanged = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (!NeedsBaseColorRepair(material))
                        continue;

                    baseColorTexture ??= ExtractFirstBaseColorTexture(glbBytes);
                    if (baseColorTexture == null)
                        return changed;

                    Material repaired = CreateTexturedLitMaterial(material, baseColorTexture, fallbackColor);
                    if (repaired == null)
                        continue;

                    materials[i] = repaired;
                    rendererChanged = true;
                    changed = true;
                }

                if (rendererChanged)
                    renderer.sharedMaterials = materials;
            }

            return changed;
        }

        static bool NeedsBaseColorRepair(Material material)
        {
            if (material == null)
                return true;

            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            bool missingOrErrorShader =
                material.shader == null ||
                shaderName.IndexOf("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0;

            if (missingOrErrorShader)
                return true;

            if (HasTexture(material, "_BaseMap") || HasTexture(material, "_MainTex") || HasTexture(material, "baseColorTexture"))
                return false;

            return shaderName.IndexOf("glTF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   shaderName.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool HasTexture(Material material, string propertyName)
        {
            return material != null &&
                   !string.IsNullOrWhiteSpace(propertyName) &&
                   material.HasProperty(propertyName) &&
                   material.GetTexture(propertyName) != null;
        }

        static Material CreateTexturedLitMaterial(Material source, Texture texture, Color fallbackColor)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Simple Lit") ??
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");
            if (shader == null)
                return null;

            Color color = ReadColor(source, fallbackColor);
            if (color.a <= 0f)
                color = Color.white;

            var material = new Material(shader)
            {
                name = source != null ? source.name + " Texture Repair" : "Generated Object Texture Repair"
            };

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            return material;
        }

        static Color ReadColor(Material material, Color fallbackColor)
        {
            if (material == null)
                return fallbackColor;
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");
            if (material.HasProperty("baseColorFactor"))
                return material.GetColor("baseColorFactor");
            return fallbackColor;
        }

        static bool TryReadFirstBaseColorImage(byte[] glbBytes, out byte[] imageBytes, out string imageName)
        {
            imageBytes = null;
            imageName = null;

            if (!TryReadGlbChunks(glbBytes, out string json, out byte[] binChunk))
                return false;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                return false;
            }

            int textureIndex = FindFirstBaseColorTextureIndex(root);
            if (textureIndex < 0)
                return false;

            JArray textures = root["textures"] as JArray;
            JArray images = root["images"] as JArray;
            JArray bufferViews = root["bufferViews"] as JArray;
            if (textures == null || images == null || bufferViews == null)
                return false;
            if (textureIndex >= textures.Count)
                return false;

            int imageIndex = textures[textureIndex]?["source"]?.Value<int>() ?? -1;
            if (imageIndex < 0 || imageIndex >= images.Count)
                return false;

            JObject image = images[imageIndex] as JObject;
            int bufferViewIndex = image?["bufferView"]?.Value<int>() ?? -1;
            imageName = image?["name"]?.Value<string>();
            if (bufferViewIndex < 0 || bufferViewIndex >= bufferViews.Count)
                return false;

            JObject bufferView = bufferViews[bufferViewIndex] as JObject;
            int byteOffset = bufferView?["byteOffset"]?.Value<int>() ?? 0;
            int byteLength = bufferView?["byteLength"]?.Value<int>() ?? -1;
            if (binChunk == null || byteOffset < 0 || byteLength <= 0 || byteOffset + byteLength > binChunk.Length)
                return false;

            imageBytes = new byte[byteLength];
            Buffer.BlockCopy(binChunk, byteOffset, imageBytes, 0, byteLength);
            return true;
        }

        static int FindFirstBaseColorTextureIndex(JObject root)
        {
            JArray materials = root["materials"] as JArray;
            if (materials == null)
                return -1;

            foreach (JToken material in materials)
            {
                int? index = material?["pbrMetallicRoughness"]?["baseColorTexture"]?["index"]?.Value<int>();
                if (index.HasValue && index.Value >= 0)
                    return index.Value;
            }

            return -1;
        }

        static bool TryReadGlbChunks(byte[] glbBytes, out string json, out byte[] binChunk)
        {
            json = null;
            binChunk = null;

            if (glbBytes == null || glbBytes.Length < 20)
                return false;
            if (ReadUInt32(glbBytes, 0) != GlbMagic)
                return false;

            int offset = 12;
            while (offset + 8 <= glbBytes.Length)
            {
                int chunkLength = (int)ReadUInt32(glbBytes, offset);
                uint chunkType = ReadUInt32(glbBytes, offset + 4);
                offset += 8;
                if (chunkLength < 0 || offset + chunkLength > glbBytes.Length)
                    return false;

                if (chunkType == JsonChunkType)
                {
                    json = Encoding.UTF8.GetString(glbBytes, offset, chunkLength).TrimEnd('\0', ' ', '\n', '\r', '\t');
                }
                else if (chunkType == BinChunkType)
                {
                    binChunk = new byte[chunkLength];
                    Buffer.BlockCopy(glbBytes, offset, binChunk, 0, chunkLength);
                }

                offset += chunkLength;
            }

            return !string.IsNullOrWhiteSpace(json) && binChunk != null;
        }

        static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                          data[offset + 1] << 8 |
                          data[offset + 2] << 16 |
                          data[offset + 3] << 24);
        }
    }
}
