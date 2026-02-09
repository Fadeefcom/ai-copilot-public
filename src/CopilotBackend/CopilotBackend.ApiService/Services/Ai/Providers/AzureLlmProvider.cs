using Azure;
using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CopilotBackend.ApiService.Services.Ai.Providers;

public class AzureLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<AzureLlmProvider> _logger;
    private readonly LatencyMonitor _latencyMonitor;

    public AzureLlmProvider(IHttpClientFactory factory, IOptions<AiOptions> options, ILogger<AzureLlmProvider> logger, LatencyMonitor latencyMonitor)
    {
        _http = factory.CreateClient("Azure");
        _options = options.Value;
        _logger = logger;
        _latencyMonitor = latencyMonitor;
    }

    public string ProviderName => "Azure";

    public async Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, string model, string? base64Image = null, CancellationToken ct = default)
    {
        var currentMessages = messages.ToList();

        if (!string.IsNullOrEmpty(base64Image))
        {
            var visionResult = await RecognizeTextWithMistralAsync(base64Image, ct);

            if (!string.IsNullOrEmpty(visionResult))
            {
                var lastUserIndex = currentMessages.FindLastIndex(m => m.Role == ChatRole.User);
                if (lastUserIndex != -1)
                {
                    var lastMsg = currentMessages[lastUserIndex];
                    currentMessages[lastUserIndex] = lastMsg with { Content = $"{lastMsg.Content}\n\n[Vision Analysis]:\n{visionResult}" };
                }
            }
            base64Image = null;
        }

        var deployment = GetDeploymentName(model);
        var url = deployment.Endpoint;
        var payload = CreatePayload(currentMessages, deployment.Name, false, null);

        return await SendRequestAsync(url, payload, ct);
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(IReadOnlyList<ChatMessage> context, string model, string? base64Image = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var currentMessages = context.ToList();

        if (!string.IsNullOrEmpty(base64Image))
        {
            var visionResult = await RecognizeTextWithMistralAsync(base64Image, ct);

            if (!string.IsNullOrEmpty(visionResult))
            {
                var lastUserIndex = currentMessages.FindLastIndex(m => m.Role == ChatRole.User);
                if (lastUserIndex != -1)
                {
                    var lastMsg = currentMessages[lastUserIndex];
                    currentMessages[lastUserIndex] = lastMsg with { Content = $"{lastMsg.Content}\n\n[Vision Analysis]:\n{visionResult}" };
                }
            }
        }

        var deployment = GetDeploymentName(model);
        var url = deployment.Endpoint;
        var payload = CreatePayload(currentMessages, model, true, null);

        await foreach (var chunk in ExecuteStreamRequestAsync(url, payload, ct).WithCancellation(ct))
        {
            yield return chunk;
        }
    }

    private async Task<string> SendRequestAsync(string url, JsonObject payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _options.ApiKey);
        request.Content = JsonContent.Create(payload);

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(request, ct);
        sw.Stop();

        _latencyMonitor.Record(sw.ElapsedMilliseconds);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private async IAsyncEnumerable<string> ExecuteStreamRequestAsync(string url, JsonObject payload, [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _options.ApiKey);
        request.Content = JsonContent.Create(payload);

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        sw.Stop();

        _latencyMonitor.Record(sw.ElapsedMilliseconds);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"LLM Error: {response.StatusCode}. Details: {error}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

            var rawData = line["data: ".Length..].Trim();
            if (rawData == "[DONE]")
            {
                yield return rawData;
                yield break;
            }

            string? content = null;
            try
            {
                var jsonNode = JsonNode.Parse(rawData);
                content = jsonNode?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            }
            catch { }

            if (!string.IsNullOrEmpty(content)) yield return content;
        }
    }

    private JsonObject CreatePayload(IEnumerable<ChatMessage> messages, string deployment, bool stream, string? base64Image)
    {
        var msgArray = new JsonArray();
        var isReasoningModel = !deployment.Equals("gpt-4o-mini", StringComparison.OrdinalIgnoreCase);
        var msgList = messages.ToList();

        for (int i = 0; i < msgList.Count; i++)
        {
            var m = msgList[i];
            string role = (m.Role == ChatRole.System && isReasoningModel) ? "developer" : m.Role.ToString().ToLowerInvariant();

            if (role == "user" && !string.IsNullOrEmpty(base64Image) && i == msgList.Count - 1)
            {
                msgArray.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = m.Content },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = $"data:image/jpeg;base64,{base64Image}" }
                        }
                    }
                });
            }
            else
            {
                msgArray.Add(new JsonObject { ["role"] = role, ["content"] = m.Content });
            }
        }

        // max_completion_tokens = budget
        var payload = new JsonObject {["model"] = deployment,  ["messages"] = msgArray, ["stream"] = stream };
        if (!isReasoningModel)
        {
            payload["temperature"] = 0.3;
            payload["top_p"] = 0.95;
        }

        return payload;
    }

    private async Task<string> RecognizeTextWithMistralAsync(string base64Image, CancellationToken ct)
    {
        var deployment = _options.VisionDeployment;
        var url = deployment.Endpoint;

        var payload = new JsonObject
        {
            ["model"] = deployment.Name, 
            ["document"] = new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = $"data:image/jpeg;base64,{base64Image}"
            },
            ["include_image_base64"] = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(payload);

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(request, ct);
        sw.Stop();

        _latencyMonitor.Record(sw.ElapsedMilliseconds);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Mistral OCR Error: {response.StatusCode}. Details: {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        if (result.TryGetProperty("pages", out var pages) && pages.GetArrayLength() > 0)
        {
            var markdown = pages[0].GetProperty("markdown").GetString();
            return markdown ?? string.Empty;
        }

        return string.Empty;
    }

    private DeploymentOptions GetDeploymentName(string model) => model.ToLower() switch
    {
        "thinking" => _options.ReasoningDeployment,
        "fast" => _options.FastDeployment,
        "vision" => _options.VisionDeployment,
        "chat" => _options.ChatDeployment,
        _ => _options.ChatDeployment
    };

    public async Task<List<(string, float[])>> GetEmbeddingAsync(IEnumerable<string> chanks, CancellationToken ct = default)
    {
        var deployment = _options.EmbeddingDeployment;

        var endpoint = new Uri(deployment.Endpoint);
        var credential = new AzureKeyCredential(_options.ApiKey);
        var model = deployment.Name;
        var deploymentName = deployment.Name;

        var openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = endpoint            
        };

        var client = new EmbeddingClient(deploymentName, credential, openAIOptions);

        var response = client.GenerateEmbeddings(chanks);

        var result = new List<(string, float[])>(response.Value.Count);

        foreach (var embedding in response.Value)
        {
            result.Add((chanks.ElementAt(embedding.Index), embedding.ToFloats().ToArray()));
        }

        return result;
    }

    public async Task<List<string>> SummarizeTextAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var systemPrompt =
            "You are an expert conversation analyzer. Your task is to process the input transcript:\n" +
            "1. Identify logical semantic blocks based on topic shifts (e.g., discussion about database, then switching to frontend, then to vacation plans).\n" +
            "2. For EACH block, write a standalone, concise summary.\n" +
            "3. Output the summaries separated strictly by the delimiter \"|||\".\n" +
            "4. Output format example: \"Summary of topic A ||| Summary of topic B ||| Summary of topic C\"\n" +
            "5. Do NOT include bullet points, numbering, or the original text. ONLY the summaries separated by the delimiter.";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, text)
        };

        var response = await GenerateResponseAsync(messages, "fast", null, ct);

        return response
            .Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }
}