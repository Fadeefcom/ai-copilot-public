using CopilotBackend.ApiService.Abstractions;

namespace CopilotBackend.ApiService.Services.Ai;

public class AiOrchestrator
{
    private readonly IEnumerable<ILlmProvider> _providers;
    private readonly PromptManager _promptManager;
    private readonly DeepgramAudioService _audioService;
    private readonly ConversationContextService _contextService;

    public AiOrchestrator(
        IEnumerable<ILlmProvider> providers,
        PromptManager promptManager,
        DeepgramAudioService audioService,
        ConversationContextService contextService)
    {
        _providers = providers;
        _promptManager = promptManager;
        _audioService = audioService;
        _contextService = contextService;
    }

    public async Task<string> ProcessRequestAsync(string connectionId, string modelName, string instruction, string? Image)
    {
        if (!_audioService.IsRunning)
            return "Audio capture is not running.";

        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];

        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) throw new ArgumentException($"Model '{modelName}' not found.");

        var messages = await _promptManager.BuildRequestMessagesAsync(connectionId, instruction, Image != null);
        return await provider.GenerateResponseAsync(messages, version, Image);
    }

    public async Task<string> ProcessAssistRequestAsync(string connectionId, string modelName, string? Image)
    {
        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];

        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) throw new ArgumentException($"Model '{modelName}' not found.");

        var messages = await _promptManager.BuildAssistMessagesAsync(connectionId, Image != null);
        return await provider.GenerateResponseAsync(messages, version, Image);
    }

    public async Task<string> ProcessFollowupRequestAsync(string connectionId, string modelName, string? Image)
    {
        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];

        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) throw new ArgumentException($"Model '{modelName}' not found.");

        var messages = await _promptManager.BuildFollowupMessagesAsync(connectionId, Image != null);
        return await provider.GenerateResponseAsync(messages, version, Image);
    }

    public async Task<string?> DetectQuestionAsync(string connectionId, string modelName, string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return null;

        var name = modelName.Split(' ')[0];
        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) return null;

        var historyText = _contextService.GetFormattedLog(connectionId, [SpeakerRole.Companion]);

        var systemPrompt =
            "You are a conversation analyzer. Your goal is to identify if the User needs help right now.\n" +
            "Instructions:\n" +
            "1. Analyze the USER BUFFER for any questions, commands.\n" +
            "2. Check DIALOGUE HISTORY. If the user repeats a question you just answered, ignore it.\n" +
            "3. If a new question is found, output the clear, concise question text.\n" +
            "4. If the user is just thinking aloud, confirming ('ok', 'right'), or there is no problem, output 'NO'.";

        var userMessage = $"--- HISTORY ---\n{historyText}\n\n--- CURRENT BUFFER ---\n{transcript}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userMessage)
        };

        try
        {
            var response = await provider.GenerateResponseAsync(messages, modelName.Split(' ')[1], null);

            if (string.IsNullOrWhiteSpace(response) ||
                response.Trim().Equals("NO", StringComparison.OrdinalIgnoreCase) ||
                response.Contains("NO.", StringComparison.OrdinalIgnoreCase))
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

    public async IAsyncEnumerable<string> StreamSmartActionAsync(AiActionType actionType, string modelName, string connectionId, string? base64Image, string? userText = null)
    {
        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];
        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);

        if (provider == null)
        {
            yield return $"System: Provider '{name}' not found.";
            yield break;
        }

        List<ChatMessage>? messages = null;
        string? errorMessage = null;

        try
        {
            bool hasImage = base64Image != null;
            messages = actionType switch
            {
                AiActionType.Assist => await _promptManager.BuildAssistMessagesAsync(connectionId, hasImage),
                AiActionType.Followup => await _promptManager.BuildFollowupMessagesAsync(connectionId, hasImage),
                AiActionType.Continue => await _promptManager.BuildContinueMessagesAsync(connectionId, hasImage),
                _ => await _promptManager.BuildRequestMessagesAsync(connectionId, userText ?? "", hasImage)
            };
        }
        catch (Exception ex)
        {
            errorMessage = $"System: Failed to load prompt. {ex.Message}";
        }

        if (errorMessage != null)
        {
            yield return errorMessage;
            yield break;
        }

        IAsyncEnumerable<string>? stream = null;
        try
        {
            stream = provider.StreamResponseAsync(messages!, version, base64Image);
        }
        catch (Exception ex)
        {
            errorMessage = $"System: Error starting stream. {ex.Message}";
        }

        if (errorMessage != null)
        {
            yield return errorMessage;
            yield break;
        }

        await foreach (var chunk in stream!)
        {
            yield return chunk;
        }
    }

    public async Task<string> SummarizeTopicsAsync(string fullTranscript, string modelName)
    {
        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];
        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);

        if (provider == null) return string.Empty;

        return await provider.GenerateResponseAsync(new[] {
            new ChatMessage(ChatRole.System, "Summarize the conversation by topics. Separate blocks with '---'"),
            new ChatMessage(ChatRole.User, fullTranscript)
        }, version);
    }

    public enum AiActionType
    {
        System,
        Assist,
        Followup,
        Continue
    }
}