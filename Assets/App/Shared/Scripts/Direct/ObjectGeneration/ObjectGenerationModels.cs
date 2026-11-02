using System;
using UnityEngine;

namespace Holodeck.Direct
{
    public enum ObjectGenerationCapability
    {
        ImageTo3D = 0,
        TextTo3D = 1
    }

    public enum ObjectGenerationProviderId
    {
        Auto = 0,
        Hitem = 1,
        ThreeDAIStudioTripo = 2
    }

    [Serializable]
    public sealed class ObjectGenerationRequest
    {
        public Texture2D image;
        public string imageSource = "";
        public string prompt = "";
        public string objectName = "";
        public string fileName = "image.jpg";
    }

    public sealed class ObjectGenerationResult
    {
        public bool success;
        public string providerName = "";
        public string taskId = "";
        public string modelUrl = "";
        public string coverUrl = "";
        public string error = "";
        public byte[] modelBytes;

        public static ObjectGenerationResult Failed(string providerName, string error)
        {
            return new ObjectGenerationResult
            {
                success = false,
                providerName = providerName,
                error = string.IsNullOrWhiteSpace(error) ? "Object generation failed." : error
            };
        }
    }

    public sealed class ObjectGenerationCreditEstimate
    {
        public bool known;
        public int requiredCredits;
        public string description = "";
    }
}
