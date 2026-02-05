using CopilotBackend.ApiService.Abstractions;

namespace CopilotBackend.ApiService.Services.Ai;

public class AiOrchestrator
{
    private readonly IEnumerable<ILlmProvider> _providers;
    private readonly PromptManager _promptManager;
    private readonly ContextManager _contextManager;
    private readonly IAudioTranscriptionService _audioService;

    public AiOrchestrator(
        IEnumerable<ILlmProvider> providers,
        PromptManager promptManager,
        ContextManager contextManager,
        IAudioTranscriptionService audioService)
    {
        _providers = providers;
        _promptManager = promptManager;
        _contextManager = contextManager;
        _audioService = audioService;
    }

    public async Task<string> ProcessRequestAsync(string modelName, string instruction, string? Image)
    {
        if (!_audioService.IsRunning)
            return "Audio capture is not running.";

        await _contextManager.CheckAndArchiveContextAsync();

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null) throw new ArgumentException($"Model 'Azure' not found.");

        var messages = await _promptManager.BuildRequestMessagesAsync(instruction, Image != null);
        return await provider.GenerateResponseAsync(messages, modelName, Image);
    }

    public async Task<string> ProcessAssistRequestAsync(string modelName, string? Image)
    {
        await _contextManager.CheckAndArchiveContextAsync();

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null) throw new ArgumentException($"Model 'Azure' not found.");

        var messages = await _promptManager.BuildAssistMessagesAsync(Image != null);
        return await provider.GenerateResponseAsync(messages, modelName, Image);
    }

    public async Task<string> ProcessFollowupRequestAsync(string modelName, string? Image)
    {
        await _contextManager.CheckAndArchiveContextAsync();

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null) throw new ArgumentException($"Model 'Azure' not found.");

        var messages = await _promptManager.BuildFollowupMessagesAsync(Image != null);
        return await provider.GenerateResponseAsync(messages, modelName, Image);
    }

    public async IAsyncEnumerable<string> StreamRequestAsync(string modelName, string prompt)
    {
        if (!_audioService.IsRunning)
        {
            yield return "System: Audio capture is not running.";
            yield break;
        }

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null)
        {
            yield return $"System: LLM Provider 'Azure' not found.";
            yield break;
        }

        IAsyncEnumerable<string>? stream = null;
        string? errorMessage = null;

        try
        {
            var messages = await _promptManager.BuildRequestMessagesAsync(prompt, false);
            stream = provider.StreamResponseAsync(messages, modelName);
        }
        catch (Exception ex)
        {
            errorMessage = $"System: Failed to initialize stream. {ex.Message}";
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

    private async IAsyncEnumerable<string> StreamResponseWithVisionAsync(string modelName, List<ChatMessage> messages, string? base64Image)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");

        if (provider == null)
        {
            yield return $"System: Provider 'Azure' not found.";
            yield break;
        }

        IAsyncEnumerable<string>? stream = null;
        string? errorMessage = null;

        try
        {
            stream = provider.StreamResponseAsync(messages, modelName, base64Image);
        }
        catch (Exception ex)
        {
            errorMessage = $"System: Error starting vision stream. {ex.Message}";
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

    private async IAsyncEnumerable<string> StreamActionWithPrompt(string modelName, string? base64Image, Task<List<ChatMessage>> promptTask)
    {
        List<ChatMessage> messages = null;
        string? errorMessage = null;

        try
        {
            messages = await promptTask;
        }
        catch (Exception ex)
        {
            errorMessage = $"System: Failed to load messages. {ex.Message}";
        }

        if (errorMessage != null)
        {
            yield return errorMessage;
            yield break;
        }

        await foreach (var chunk in StreamResponseWithVisionAsync(modelName, messages!, base64Image))
        {
            yield return chunk;
        }
    }

    public async Task<string?> DetectQuestionAsync(string modelName, string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return null;

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null) return null;

        var historyMessages = _contextManager.GetMessages()
            .Where(m => m.Timestamp > DateTime.UtcNow.AddMinutes(-15))
            .TakeLast(6)
            .ToList();

        var historyText = string.Join("\n", historyMessages.Select(m => $"[{m.Role}]: {m.Text}"));

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
            var response = await provider.GenerateResponseAsync(messages, modelName, null);

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

    public IAsyncEnumerable<string> StreamSmartActionAsync(AiActionType actionType, string modelName, string? base64Image, string? userText = null)
    {
        var ifImage = !string.IsNullOrWhiteSpace(base64Image);

        return actionType switch
        {
            AiActionType.Assist => StreamActionWithPrompt(modelName, base64Image, _promptManager.BuildAssistMessagesAsync(ifImage)),    
            AiActionType.Followup => StreamActionWithPrompt(modelName, base64Image, _promptManager.BuildFollowupMessagesAsync(ifImage)),
            _ => StreamActionWithPrompt(modelName, base64Image, _promptManager.BuildRequestMessagesAsync(userText, ifImage))
        };
    }

    public enum AiActionType
    {
        System,
        Assist,
        Followup
    }
}