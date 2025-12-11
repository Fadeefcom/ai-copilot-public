using CopilotBackend.ApiService.Abstractions;

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
}