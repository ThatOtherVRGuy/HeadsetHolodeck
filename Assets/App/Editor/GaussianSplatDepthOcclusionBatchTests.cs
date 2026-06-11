using System;
using System.IO;
using UnityEngine;

namespace HeadsetHolodeck.EditorTests
{
    public static class GaussianSplatDepthOcclusionBatchTests
    {
        public static void RenderShaderSamplesCameraDepthBeforeWritingSplats()
        {
            string shaderPath = Path.Combine(Application.dataPath, "../Packages/com.worldlabs.gaussian-splatting/Shaders/RenderGaussianSplats.shader");
            string shader = File.ReadAllText(shaderPath);

            if (!shader.Contains("Texture2D _CameraDepthTexture"))
                throw new Exception("RenderGaussianSplats.shader must sample _CameraDepthTexture.");

            if (!shader.Contains("UNITY_REVERSED_Z"))
                throw new Exception("RenderGaussianSplats.shader must handle reversed-Z depth comparisons.");

            if (!shader.Contains("discard"))
                throw new Exception("RenderGaussianSplats.shader must discard splats hidden behind opaque scene depth.");

            Debug.Log("[GaussianSplatDepthOcclusionBatchTests] RenderShaderSamplesCameraDepthBeforeWritingSplats passed.");
        }
    }
}
