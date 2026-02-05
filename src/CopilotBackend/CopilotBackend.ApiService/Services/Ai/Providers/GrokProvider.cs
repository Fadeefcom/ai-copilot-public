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

    public string ProviderName => "Grok";

    public async Task<string> GenerateResponseAsync(
        IEnumerable<ChatMessage> messages,
        string model,
        string? base64Image = null,
        CancellationToken ct = default)
    {
        var modelToUse = !string.IsNullOrEmpty(model) ? model : _defaultModel;

        _logger.LogInformation("Generating response using Grok model: {Model}", modelToUse);

        var request = CreateRequest(messages, modelToUse, false, base64Image);

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
        string model,
        string? base64Image = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var modelToUse = !string.IsNullOrEmpty(model) ? model : _defaultModel;

        _logger.LogInformation("Starting stream using Grok model: {Model}", modelToUse);

        var request = CreateRequest(context, modelToUse, true, base64Image);

        HttpResponseMessage? responseMessage = null;
        string? errorMessage = null;

        try
        {
            responseMessage = await _api.ChatStreamAsync(request, _apiKey);
            responseMessage.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate stream with Grok model: {Model}", modelToUse);
            errorMessage = $"System: Grok Error - {responseMessage?.StatusCode.ToString()}";
        }

        if (!string.IsNullOrEmpty(errorMessage))
        {
            yield return errorMessage;
            yield break;
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

    private JsonObject CreateRequest(IEnumerable<ChatMessage> messages, string model, bool stream, string? base64Image)
    {
        var messageArray = new JsonArray();
        var msgList = messages.ToList();

        for (int i = 0; i < msgList.Count; i++)
        {
            var msg = msgList[i];
            var isLast = i == msgList.Count - 1;

            if (isLast && msg.Role == ChatRole.User && !string.IsNullOrEmpty(base64Image))
            {
                messageArray.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = msg.Content
                        },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = $"data:image/jpeg;base64,{base64Image}"
                            }
                        }
                    }
                });
            }
            else
            {
                messageArray.Add(new JsonObject
                {
                    ["role"] = msg.Role.ToString().ToLowerInvariant(),
                    ["content"] = msg.Content
                });
            }
        }

        return new JsonObject
        {
            ["model"] = model,
            ["messages"] = messageArray,
            ["temperature"] = 0.4,
            ["stream"] = stream
        };
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