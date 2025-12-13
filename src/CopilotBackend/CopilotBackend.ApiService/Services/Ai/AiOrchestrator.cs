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

        var name = modelName.Split('_')[0];
        var version = modelName.Split("_")[1];

        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) throw new ArgumentException($"Model '{name}' not found.");

        var messages = await _promptManager.BuildRequestMessagesAsync(instruction);
        return await provider.GenerateResponseAsync(messages, version);
    }

    public async IAsyncEnumerable<string> StreamRequestAsync(string modelName, string prompt)
    {
        if (!_audioService.IsRunning)
        {
            yield return "Audio capture is not running.";
            yield break;
        }

        var provider = _providers.FirstOrDefault(p => p.ProviderName == modelName);
        if (provider == null)
        {
            yield return $"Error: LLM Provider '{modelName}' not found.";
            yield break;
        }

        var systemPrompt = await _promptManager.GetSystemPrompt();

        var messages = new List<ChatMessage>
        {
            new(SpeakerRole.System.ToString(), systemPrompt),
            new(SpeakerRole.Me.ToString(), prompt)
        };

        await foreach (var chunk in provider.StreamResponseAsync(messages))
        {
            yield return chunk;
        }
    }

    public async Task<string?> DetectQuestionAsync(string modelName, string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript) || transcript.Length < 10)
            return null;

        var provider = _providers.FirstOrDefault(p => p.ProviderName == modelName);
        if (provider == null)
            return $"Error: LLM Provider '{modelName}' not found.";

        var systemPrompt =
            "You are a semantic detector. Your task is to analyze the user's speech buffer. " +
            "Check if it contains a COMPLETE, addressed question that requires an answer. " +
            "If a distinct question is present, extract and output ONLY the question text. " +
            "If the text is incomplete, just conversational filler, or does not contain a question, output 'NO'.";

        var messages = new List<ChatMessage>
        {
            new(SpeakerRole.System.ToString(), systemPrompt),
            new(SpeakerRole.Me.ToString(), transcript)
        };

        try
        {
            var response = await provider.GenerateResponseAsync(messages);

            if (string.IsNullOrWhiteSpace(response) || response.Contains("NO", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return response.Trim();
        }
        catch
        {
            return null;
        }
    }
}