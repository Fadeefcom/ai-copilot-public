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

    public string ProviderName => "Grok (grok-4)";

    public GrokProvider(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _apiKey = options.Value.GroqApiKey;
    }

    public async Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var request = new
        {
            model = "grok-4-latest",
            messages = messages.Select(m => new { role = m.Role, content = m.Content })
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