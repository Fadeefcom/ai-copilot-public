using System.Text;
using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Services;

namespace CopilotBackend.ApiService.Services;

public class GrokService : IAiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly DeepgramAudioService _audioService;
    private readonly ConversationContextService _contextService;
    private readonly ContextManager _contextManager;
    private readonly string _promptsFolder = "promts";
    private readonly string _userContextFile = "user.md";
    private readonly string _systemPromptFile = "system.md";

    public GrokService(
        IConfiguration config,
        HttpClient http,
        DeepgramAudioService audioService,
        ConversationContextService contextService,
        ContextManager contextManager)
    {
        _http = http;
        _apiKey = config["ApiKeys:GROQ_API_KEY"]
                      ?? Environment.GetEnvironmentVariable("GROQ_API_KEY")
                      ?? throw new InvalidOperationException("xAI (Grok) API key not configured");

        _audioService = audioService;
        _contextService = contextService;
        _contextManager = contextManager;
    }

    public string ModelName => "Grok (grok-1)";

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

        await _contextManager.CheckAndArchiveContextAsync();

        var systemPrompt = LoadPrompt(_systemPromptFile);
        var userContext = LoadPrompt(_userContextFile);

        var fullSystemMessage = new StringBuilder();
        fullSystemMessage.AppendLine("--- SYSTEM INSTRUCTIONS ---");
        fullSystemMessage.AppendLine(systemPrompt);
        fullSystemMessage.AppendLine("--- USER PERSONA (ME) ---");
        fullSystemMessage.AppendLine(userContext);

        var dialogueHistory = _contextService.GetFormattedLog();
        var fullUserMessage = new StringBuilder();
        fullUserMessage.AppendLine("--- CURRENT DIALOGUE TRANSCRIPT ---");
        fullUserMessage.AppendLine(dialogueHistory);
        fullUserMessage.AppendLine("--- YOUR TASK ---");
        fullUserMessage.AppendLine(instructionKey);
        fullUserMessage.AppendLine("--- GENERATE NEXT RESPONSE FOR 'ME' NOW ---");

        var messages = new object[]
        {
        new
        {
            role = "system",
            content = new[] { new { type = "text", text = fullSystemMessage.ToString() } }
        },
        new
        {
            role = "user",
            content = new[] { new { type = "text", text = fullUserMessage.ToString() } }
        }
        };

        var request = new
        {
            model = "grok-4-0709",
            messages,
            temperature = 0.4
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/chat/completions")
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        httpReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _http.SendAsync(httpReq);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var messageElement))
            {
                if (messageElement.TryGetProperty("content", out var contentElement))
                {
                    if (contentElement.ValueKind == System.Text.Json.JsonValueKind.Array && contentElement.GetArrayLength() > 0)
                    {
                        var firstBlock = contentElement[0];
                        if (firstBlock.TryGetProperty("text", out var textProp))
                            return textProp.GetString() ?? "";
                        if (firstBlock.TryGetProperty("content", out var altProp))
                            return altProp.GetString() ?? "";
                    }
                    else if (contentElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return contentElement.GetString() ?? "";
                    }
                }
            }
        }

        return "";
    }
}