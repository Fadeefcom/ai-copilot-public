using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using Microsoft.Extensions.Options;

namespace CopilotBackend.ApiService.Services.Ai.Providers;

public class GrokProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    // Имя должно совпадать с тем, что отправляет UI (main_ui.py: MODELS = ["Grok (grok-4)", ...])
    public string ProviderName => "Grok (grok-4)";

    public GrokProvider(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        // Используем ключ, который мапится на "GROQ_API_KEY" в конфиге, 
        // так как в оригинале использовалась именно эта переменная для xAI.
        _apiKey = options.Value.GroqApiKey;
    }

    public async Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var request = new
        {
            model = "grok-beta", // Или "grok-4-0709" как было в оригинале, если у вас есть доступ
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            temperature = 0.4
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(httpReq, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        return ParseXAiResponse(json);
    }

    private string ParseXAiResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var messageElement))
            {
                if (messageElement.TryGetProperty("content", out var contentElement))
                {
                    // Логика из оригинального GrokService для обработки разных форматов (массив/строка)
                    if (contentElement.ValueKind == JsonValueKind.Array && contentElement.GetArrayLength() > 0)
                    {
                        var firstBlock = contentElement[0];
                        if (firstBlock.TryGetProperty("text", out var textProp))
                            return textProp.GetString() ?? "";
                        if (firstBlock.TryGetProperty("content", out var altProp))
                            return altProp.GetString() ?? "";
                    }
                    else if (contentElement.ValueKind == JsonValueKind.String)
                    {
                        return contentElement.GetString() ?? "";
                    }
                }
            }
        }
        return "";
    }
}