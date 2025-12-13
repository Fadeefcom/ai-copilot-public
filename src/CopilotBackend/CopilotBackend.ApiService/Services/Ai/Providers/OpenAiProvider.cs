using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace CopilotBackend.ApiService.Services.Ai.Providers;

public class OpenAiProvider : ILlmProvider
{
    private readonly IOpenAiApi _api;
    private readonly ILogger<OpenAiProvider> _logger;
    private readonly string _apiKey;

    public OpenAiProvider(
        IOpenAiApi api,
        ILogger<OpenAiProvider> logger,
        IOptions<AiOptions> options)
    {
        _api = api;
        _logger = logger;
        _apiKey = $"Bearer {options.Value.OpenAiApiKey}";
    }

    public string ProviderName => "OpenAI";

    public async Task<string> GenerateResponseAsync(
        IEnumerable<ChatMessage> messages,
        string model,
        CancellationToken ct = default)
    {
        var modelToUse = model;

        _logger.LogInformation("Generating response using model: {Model}", modelToUse);

        var request = new JsonObject
        {
            ["model"] = modelToUse,
            ["messages"] = new JsonArray(messages.Select(m => new JsonObject
            {
                ["role"] = m.Role.ToString().ToLowerInvariant(),
                ["content"] = m.Content
            }).ToArray()),
            ["temperature"] = 0.4,
            ["top_p"] = 0.95
        };

        try
        {
            var response = await _api.ChatCompletionAsync(request, _apiKey);
            var content = response?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Received empty content from OpenAI model: {Model}", modelToUse);
                return string.Empty;
            }

            _logger.LogInformation("Successfully generated response. Length: {Length}", content.Length);
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response from OpenAI model: {Model}", modelToUse);
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        IReadOnlyList<ChatMessage> context,
        string model,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var modelToUse = model;

        _logger.LogInformation("Starting stream using model: {Model}", modelToUse);

        var request = new JsonObject
        {
            ["model"] = modelToUse,
            ["messages"] = new JsonArray(context.Select(m => new JsonObject
            {
                ["role"] = m.Role.ToString().ToLowerInvariant(),
                ["content"] = m.Content
            }).ToArray()),
            ["stream"] = true,
            ["temperature"] = 0.4,
            ["top_p"] = 0.95
        };

        HttpResponseMessage? responseMessage = null;

        try
        {
            responseMessage = await _api.ChatStreamAsync(request, _apiKey);
            responseMessage.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate stream with OpenAI model: {Model}", modelToUse);
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

            var data = line.AsSpan(6);

            if (data.SequenceEqual("[DONE]"))
            {
                _logger.LogInformation("Stream finished for model: {Model}", modelToUse);
                yield break;
            }

            string? content = null;

            try
            {
                var jsonNode = JsonNode.Parse(data.ToString());
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
}