using CopilotBackend.ApiService.Services;

namespace CopilotBackend.ApiService.Abstractions;

public interface IAudioTranscriptionService : IDisposable
{
    bool IsRunning { get; }
    Task StartAsync(string language);
    Task PushAudio(SpeakerRole role, byte[] data);
    Task StopAsync();
    string PopNewText();
    string? GetAndClearCompleteQuestion();
    void Clear();
}
