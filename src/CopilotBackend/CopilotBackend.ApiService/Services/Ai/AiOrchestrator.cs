using CopilotBackend.ApiService.Abstractions;
using System.Text;

namespace CopilotBackend.ApiService.Services.Ai;

public class AiOrchestrator
{
    private readonly IEnumerable<ILlmProvider> _providers;
    private readonly PromptManager _promptManager;
    private readonly ConversationContextService _contextManager;
    private readonly IAudioTranscriptionService _audioService;
    private readonly IVectorDbService _vectorDbService;

    public AiOrchestrator(
        IEnumerable<ILlmProvider> providers,
        PromptManager promptManager,
        ConversationContextService contextManager,
        IAudioTranscriptionService audioService,
        IVectorDbService vectorDbService)
    {
        _providers = providers;
        _promptManager = promptManager;
        _contextManager = contextManager;
        _audioService = audioService;
        _vectorDbService = vectorDbService;
    }

    private async Task<string?> RetrieveMemoryAsync(string query, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(query) || userId == Guid.Empty) return null;

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null) return null;

        try
        {
            var embeddings = await provider.GetEmbeddingAsync(new[] { query });
            if (embeddings.Count == 0) return null;

            var vector = embeddings[0].Item2;

            var results = await _vectorDbService.SearchAsync("copilot-memory", query, vector, userId, limit: 3);

            if (results.Count == 0) return null;

            return string.Join("\n\n", results.Select((r, i) => $"[Memory {i + 1}]: {r}"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Orchestrator] Memory search failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> DetectMemorySearchQueryAsync(string userQuery)
    {
        if (string.IsNullOrWhiteSpace(userQuery)) return null;

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null) return null;

        var systemPrompt =
            "You are a Search Query Optimizer for a vector database.\n" +
            "Your Task:\n" +
            "1. Analyze the user's input to determine if they need information from Long-Term Memory (specific projects, past conversations, biography, stored facts).\n" +
            "2. IF NO SEARCH NEEDED (general chit-chat, basic coding questions, math): Output 'NO'.\n" +
            "3. IF SEARCH NEEDED: Formulate a concise, keyword-rich search query best suited for retrieval.\n\n" +
            "Examples:\n" +
            "User: \"What was the error in the last deployment?\" -> Output: \"deployment error log exception\"\n" +
            "User: \"Remind me about architecture\" -> Output: \"project architecture design\"\n" +
            "User: \"Hi, how are you?\" -> Output: \"NO\"\n" +
            "User: \"Write a binary search in C#\" -> Output: \"NO\"";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userQuery)
        };

        try
        {
            var response = await provider.GenerateResponseAsync(messages, "fast");
            var cleaned = response.Trim();

            if (cleaned.Equals("NO", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("NO.", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return cleaned.Replace("\"", "").Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> ProcessRequestAsync(string modelName, string instruction, string? Image)
    {
        if (!_audioService.IsRunning)
            return "Audio capture is not running.";

        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null) throw new ArgumentException($"Model 'Azure' not found.");

        var messages = await _promptManager.BuildRequestMessagesAsync(instruction, Image != null);
        return await provider.GenerateResponseAsync(messages, modelName, Image);
    }

    public async Task<string> ProcessAssistRequestAsync(string modelName, string? Image)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == "Azure");
        if (provider == null) throw new ArgumentException($"Model 'Azure' not found.");

        var messages = await _promptManager.BuildAssistMessagesAsync(Image != null);
        return await provider.GenerateResponseAsync(messages, modelName, Image);
    }

    public async Task<string> ProcessFollowupRequestAsync(string modelName, string? Image)
    {
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
        List<ChatMessage>? messages = null;
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

        StringBuilder responce = new StringBuilder();
        await foreach (var chunk in StreamResponseWithVisionAsync(modelName, messages!, base64Image))
        {
            responce.AppendLine(chunk);
            yield return chunk;
        }
        
        _contextManager.AddAssistantMessage(responce.ToString());
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

    public async IAsyncEnumerable<string> StreamSmartActionAsync(Guid userId, AiActionType actionType, string modelName, string? base64Image, string? userText = null, bool forceMemory = false)
    {
        var ifImage = !string.IsNullOrWhiteSpace(base64Image);
        string? searchQuery = null;
        string? retrievedContext = null;
        string? rawQuery = userText;

        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            var lastMsgs = _contextManager.GetMessages().TakeLast(20).Select(m => m.Text);
            rawQuery = string.Join(" ", lastMsgs);
        }

        if (forceMemory && !string.IsNullOrWhiteSpace(rawQuery))
        {
            searchQuery = rawQuery;
            yield return "System: Memory search forced.";
        }
        else if (!string.IsNullOrWhiteSpace(rawQuery))
        {
            searchQuery = await DetectMemorySearchQueryAsync(rawQuery);
            if (searchQuery != null)
            {
                yield return $"System: Auto-detected search intent. Query: \"{searchQuery}\"";
            }
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            retrievedContext = await RetrieveMemoryAsync(searchQuery, userId);

            if (retrievedContext != null)
                yield return "System: Context found.";
            else if (forceMemory)
                yield return "System: Nothing found in memory.";
        }

        var task = actionType switch
        {
            AiActionType.Assist => StreamActionWithPrompt(modelName, base64Image, _promptManager.BuildAssistMessagesAsync(ifImage, retrievedContext)),
            AiActionType.Followup => StreamActionWithPrompt(modelName, base64Image, _promptManager.BuildFollowupMessagesAsync(ifImage, retrievedContext)),
            AiActionType.WhatToSay => StreamActionWithPrompt(modelName, base64Image, _promptManager.BuildWhatToSay(ifImage, retrievedContext)),
            _ => StreamActionWithPrompt(modelName, base64Image, _promptManager.BuildRequestMessagesAsync(userText ?? string.Empty, ifImage , retrievedContext))
        };

        await foreach (var chunk in task)
        {
            yield return chunk;
        }
    }

    public enum AiActionType
    {
        System,
        Assist,
        Followup,
        WhatToSay
    }
}