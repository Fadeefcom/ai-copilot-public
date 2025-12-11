using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using Microsoft.Extensions.Options;

namespace CopilotBackend.ApiService.Services.Ai.Providers;

public class OpenAiProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public OpenAiProvider(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _apiKey = options.Value.OpenAiApiKey;
    }

    public string ProviderName => "OpenAI GPT-4.1 Mini";

    public async Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var request = new
        {
            model = "gpt-5-nano",
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            temperature = 0.4,
            top_p = 0.95
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _httpClient.SendAsync(httpReq, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}