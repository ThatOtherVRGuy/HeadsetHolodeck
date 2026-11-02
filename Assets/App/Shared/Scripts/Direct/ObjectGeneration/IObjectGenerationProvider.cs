using System;
using System.Collections;

namespace Holodeck.Direct
{
    public interface IObjectGenerationProvider
    {
        string ProviderName { get; }
        bool IsConfigured { get; }

        bool SupportsCapability(ObjectGenerationCapability capability);
        ObjectGenerationCreditEstimate EstimateCredits(ObjectGenerationCapability capability);
        IEnumerator GenerateFromImage(ObjectGenerationRequest request, Action<ObjectGenerationResult> onComplete);
        IEnumerator GenerateFromText(ObjectGenerationRequest request, Action<ObjectGenerationResult> onComplete);
    }
}
