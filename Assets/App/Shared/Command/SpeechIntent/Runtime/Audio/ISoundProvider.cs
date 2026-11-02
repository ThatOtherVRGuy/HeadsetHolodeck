using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpeechIntent.Audio
{
    public interface ISoundProvider
    {
        string ProviderName { get; }
        bool IsConfigured { get; }
        bool CanHandle(SoundQuery query);
        Task<List<SoundSearchResult>> SearchAsync(SoundQuery query, int limit);
        Task<byte[]> DownloadAudioBytesAsync(SoundSearchResult result);
    }
}
