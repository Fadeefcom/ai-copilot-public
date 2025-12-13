using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CopilotBackend.ApiService.Services.Ai.Providers;

public class GrokProvider : ILlmProvider
{
    private readonly IGrokApi _api;
    private readonly ILogger<GrokProvider> _logger;
    private readonly string _apiKey;
    private readonly string _defaultModel;

    public GrokProvider(
        IGrokApi api,
        ILogger<GrokProvider> logger,
        IOptions<AiOptions> options)
    {
        _api = api;
        _logger = logger;
        _apiKey = $"Bearer {options.Value.GroqApiKey}";
        _defaultModel = "grok-beta";
    }

    public string ProviderName => "Grok (xAI)";

    public async Task<string> GenerateResponseAsync(
        IEnumerable<ChatMessage> messages,
        string? overrideModel = null,
        CancellationToken ct = default)
    {
        var modelToUse = overrideModel ?? _defaultModel;

        _logger.LogInformation("Generating response using Grok model: {Model}", modelToUse);

        var request = new JsonObject
        {
            ["model"] = modelToUse,
            ["messages"] = new JsonArray(messages.Select(m => new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToArray()),
            ["temperature"] = 0.4,
            ["stream"] = false
        };

        try
        {
            var response = await _api.ChatCompletionAsync(request, _apiKey);
            return ParseGrokResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response from Grok model: {Model}", modelToUse);
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        IReadOnlyList<ChatMessage> context,
        string? overrideModel = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var modelToUse = overrideModel ?? _defaultModel;

        _logger.LogInformation("Starting stream using Grok model: {Model}", modelToUse);

        var request = new JsonObject
        {
            ["model"] = modelToUse,
            ["messages"] = new JsonArray(context.Select(m => new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToArray()),
            ["stream"] = true,
            ["temperature"] = 0.4
        };

        HttpResponseMessage? responseMessage = null;

        try
        {
            responseMessage = await _api.ChatStreamAsync(request, _apiKey);
            responseMessage.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate stream with Grok model: {Model}", modelToUse);
            throw;
        }

        using var stream = await responseMessage.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            string? line = null;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading stream line");
                throw;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            if (line.AsSpan(6).SequenceEqual("[DONE]"))
            {
                _logger.LogInformation("Stream finished for Grok model: {Model}", modelToUse);
                yield break;
            }

            string? content = null;
            try
            {
                var jsonNode = JsonNode.Parse(line[6..]);
                content = jsonNode?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing stream chunk");
            }

            if (!string.IsNullOrEmpty(content))
            {
                yield return content;
            }
        }
    }

    private string ParseGrokResponse(JsonObject? response)
    {
        if (response == null) return string.Empty;

        var message = response["choices"]?[0]?["message"];
        if (message == null) return string.Empty;

        var contentNode = message["content"];
        if (contentNode == null) return string.Empty;

        if (contentNode.GetValueKind() == JsonValueKind.String)
        {
            return contentNode.GetValue<string>();
        }

        if (contentNode.GetValueKind() == JsonValueKind.Array)
        {
            var array = contentNode.AsArray();
            if (array.Count > 0)
            {
                var firstBlock = array[0];
                return firstBlock?["text"]?.GetValue<string>()
                    ?? firstBlock?["content"]?.GetValue<string>()
                    ?? string.Empty;
            }
        }

        return string.Empty;
    }
}