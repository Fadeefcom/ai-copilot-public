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

    public async Task<string> ProcessRequestAsync(string modelName, string instruction, string? Image)
    {
        if (!_audioService.IsRunning)
            return "Audio capture is not running.";

        await _contextManager.CheckAndArchiveContextAsync();

        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];

        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) throw new ArgumentException($"Model '{modelName}' not found.");

        var messages = await _promptManager.BuildRequestMessagesAsync(instruction, Image != null);
        return await provider.GenerateResponseAsync(messages, version, Image);
    }

    public async Task<string> ProcessAssistRequestAsync(string modelName, string? Image)
    {
        await _contextManager.CheckAndArchiveContextAsync();

        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];

        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) throw new ArgumentException($"Model '{modelName}' not found.");

        var messages = await _promptManager.BuildAssistMessagesAsync(Image != null);
        return await provider.GenerateResponseAsync(messages, version, Image);
    }

    public async Task<string> ProcessFollowupRequestAsync(string modelName, string? Image)
    {
        await _contextManager.CheckAndArchiveContextAsync();

        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];

        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) throw new ArgumentException($"Model '{modelName}' not found.");

        var messages = await _promptManager.BuildFollowupMessagesAsync(Image != null);
        return await provider.GenerateResponseAsync(messages, version, Image);
    }

    public async IAsyncEnumerable<string> StreamRequestAsync(string modelName, string prompt)
    {
        if (!_audioService.IsRunning)
        {
            yield return "Audio capture is not running.";
            yield break;
        }

        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];

        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null)
        {
            yield return $"Error: LLM Provider '{modelName}' not found.";
            yield break;
        }

        var systemPrompt = await _promptManager.GetSystemPrompt();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, prompt)
        };

        await foreach (var chunk in provider.StreamResponseAsync(messages, version))
        {
            yield return chunk;
        }
    }

    public async Task<string?> DetectQuestionAsync(string modelName, string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return null;

        var name = modelName.Split(' ')[0];
        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
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

    public async IAsyncEnumerable<string> StreamResponseWithVisionAsync(string modelName, string prompt, string? base64Image)
    {
        var name = modelName.Split(' ')[0];
        var version = modelName.Split(' ')[1];
        var provider = _providers.FirstOrDefault(p => p.ProviderName == name);
        if (provider == null) yield break;

        var systemPrompt = await _promptManager.GetSystemPrompt();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, prompt)
        };

        await foreach (var chunk in provider.StreamResponseAsync(messages, version, base64Image))
        {
            yield return chunk;
        }
    }
}