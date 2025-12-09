using System.Text;
using CopilotBackend.ApiService.Abstractions;

namespace CopilotBackend.ApiService.Services;

public class OpenAiService : IAiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly DeepgramAudioService _audioService;
    private readonly ConversationContextService _contextService;
    private readonly ContextManager _contextManager;
    private readonly string _promptsFolder = "promts";
    private readonly string _userContextFile = "user.md";
    private readonly string _systemPromptFile = "system.md";

    public OpenAiService(
        IConfiguration config,
        HttpClient http,
        DeepgramAudioService audioService,
        ConversationContextService contextService,
        ContextManager contextManager)
    {
        _http = http;
        _apiKey = config["ApiKeys:OPENAI_API_KEY"]
                      ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                      ?? throw new InvalidOperationException("OpenAI API key not configured");

        _audioService = audioService;
        _contextService = contextService;
        _contextManager = contextManager;
    }

    public string ModelName => "OpenAI GPT-4.1 Mini";

    private string LoadPrompt(string fileName)
    {
        var path = Path.Combine(_promptsFolder, fileName);
        if (!File.Exists(path)) return "";
        return File.ReadAllText(path);
    }

    public async Task<string> SendMessageWithContextAsync(string instructionKey)
    {
        if (!_audioService.IsRunning)
            return "Audio capture is not running";

        // **********************************************
        // Вызов менеджера архивации
        // **********************************************
        await _contextManager.CheckAndArchiveContextAsync();

        // 1. Сборка System-сообщения
        var systemPrompt = LoadPrompt(_systemPromptFile);
        var userContext = LoadPrompt(_userContextFile);

        var fullSystemMessage = new StringBuilder();
        fullSystemMessage.AppendLine("--- SYSTEM INSTRUCTIONS ---");
        fullSystemMessage.AppendLine(systemPrompt);
        fullSystemMessage.AppendLine("--- USER PERSONA (ME) ---");
        fullSystemMessage.AppendLine(userContext);

        var messages = new List<Dictionary<string, string>>
        {
            new() { ["role"] = "system", ["content"] = fullSystemMessage.ToString() }
        };

        // 2. Сборка User-сообщения
        var dialogueHistory = _contextService.GetFormattedLog();
        var currentInstruction = instructionKey;

        var fullUserMessage = new StringBuilder();
        fullUserMessage.AppendLine("--- CURRENT DIALOGUE TRANSCRIPT ---");
        fullUserMessage.AppendLine(dialogueHistory);
        fullUserMessage.AppendLine("--- YOUR TASK ---");
        fullUserMessage.AppendLine(currentInstruction);
        fullUserMessage.AppendLine("--- GENERATE NEXT RESPONSE FOR 'ME' NOW ---");

        messages.Add(new() { ["role"] = "user", ["content"] = fullUserMessage.ToString() });

        // 3. Отправка запроса
        var request = new
        {
            model = "gpt-4.1-mini",
            messages,
            temperature = 0.4,
            top_p = 0.95
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _http.SendAsync(httpReq);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        return content ?? "";
    }
}