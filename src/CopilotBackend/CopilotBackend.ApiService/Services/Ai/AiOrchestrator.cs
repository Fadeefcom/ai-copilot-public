using CopilotBackend.ApiService.Abstractions;
using Deepgram.Models.Agent.v2.WebSocket;

namespace CopilotBackend.ApiService.Services.Ai;

public class AiOrchestrator
{
    private readonly IEnumerable<ILlmProvider> _providers;
    private readonly PromptManager _promptManager;
    private readonly ContextManager _contextManager;
    private readonly DeepgramAudioService _audioService;

    public AiOrchestrator(
        IEnumerable<ILlmProvider> providers,
        PromptManager promptManager,
        ContextManager contextManager,
        DeepgramAudioService audioService)
    {
        _providers = providers;
        _promptManager = promptManager;
        _contextManager = contextManager;
        _audioService = audioService;
    }

    public async Task<string> ProcessRequestAsync(string modelName, string instruction)
    {
        if (!_audioService.IsRunning)
            return "Audio capture is not running.";

        await _contextManager.CheckAndArchiveContextAsync();

        var provider = _providers.FirstOrDefault(p => p.ProviderName == modelName);
        if (provider == null) throw new ArgumentException($"Model '{modelName}' not found.");

        var messages = await _promptManager.BuildRequestMessagesAsync(instruction);
        return await provider.GenerateResponseAsync(messages);
    }

    public IAsyncEnumerable<string> StreamRequestAsync(string modelName, string prompt)
    {
        if (!_audioService.IsRunning)
            return new List<string> { "Audio capture is not running." }.ToAsyncEnumerable();

        var provider = _providers.FirstOrDefault(p => p.ProviderName == modelName);
        if (provider == null)
        {
            return new List<string> { $"Error: LLM Provider '{modelName}' not found." }.ToAsyncEnumerable();
        }


        var systemPrompt = _promptManager.GetSystemPrompt();
        var messages = new List<ChatMessage>
        {
            new(SpeakerRole.System.ToString(), systemPrompt) { },
            new(SpeakerRole.Me.ToString(), prompt) { }
        };

        return provider.StreamResponseAsync(messages);
    }    
}

public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
    }
}