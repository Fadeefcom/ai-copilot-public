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
            model = "gpt-4.1-mini",
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

    public async IAsyncEnumerable<string> StreamResponseAsync(IReadOnlyList<ChatMessage> context)
    {
        var request = new
        {
            model = "gpt-4.1-mini",
            messages = context.Select(m => new { role = m.Role, content = m.Content }),
            temperature = 0.4,
            stream = true
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _httpClient.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("data: [DONE]")) break;

            if (line.StartsWith("data: "))
            {
                var json = line.Substring(6);
                string? content = null;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var contentProp))
                        {
                            content = contentProp.GetString();
                        }
                    }
                }
                catch { /* ignore parsing errors */ }

                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                }
            }
        }
    }
}