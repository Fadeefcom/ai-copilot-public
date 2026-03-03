using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using CopilotBackend.ApiService.Services.Ai.Providers;
using FaissMask;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CopilotBackend.ApiService.Services;

public class VectorRecord
{
    public string SessionId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

public class SummarizationFaissWorker
{
    private readonly IEnumerable<ILlmProvider> _providers;
    private readonly ILogger<SummarizationFaissWorker> _logger;
    private readonly IOpenAiApi _openAiApi;
    private readonly AiOptions _aiOptions;

    private readonly string _knowledgeBasePath = "knowledge_base.json";

    public SummarizationFaissWorker(
        IEnumerable<ILlmProvider> providers,
        ILogger<SummarizationFaissWorker> logger,
        IOpenAiApi openAiApi,
        IOptions<AiOptions> aiOptions)
    {
        _providers = providers;
        _logger = logger;
        _openAiApi = openAiApi;
        _aiOptions = aiOptions.Value;
    }

    public async Task ProcessSessionAsync(string sessionId, List<ConversationMessage> history)
    {
        try
        {
            _logger.LogInformation("Starting background summarization for session: {SessionId}", sessionId);

            var fullTranscript = new StringBuilder();
            foreach (var msg in history)
            {
                fullTranscript.AppendLine($"{msg.Role}: {msg.Text}");
            }

            var provider = _providers.FirstOrDefault(p => p.ProviderName == "OpenAI");

            if (provider == null)
                return;

            var prompt = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "You are a data structurer. Analyze the conversation. Break it down into logical topics/blocks. Separate each block exactly with the delimiter '###BLOCK###'."),
                new ChatMessage(ChatRole.User, fullTranscript.ToString())
            };

            var summaryResult = await provider.GenerateResponseAsync(prompt, "gpt-4o-mini");

            var semanticBlocks = summaryResult
                .Split("###BLOCK###", StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrEmpty(b))
                .ToList();

            if (semanticBlocks.Any())
            {
                await SaveToFaissLocalAsync(sessionId, semanticBlocks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Summarization Worker processing.");
        }
    }

    private async Task SaveToFaissLocalAsync(string sessionId, List<string> blocks)
    {
        var embeddings = await GetEmbeddingsAsync(blocks);
        if (!embeddings.Any()) return;

        var records = new List<VectorRecord>();
        if (File.Exists(_knowledgeBasePath))
        {
            var json = await File.ReadAllTextAsync(_knowledgeBasePath);
            if (!string.IsNullOrWhiteSpace(json))
            {
                records = JsonSerializer.Deserialize<List<VectorRecord>>(json) ?? new List<VectorRecord>();
            }
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            records.Add(new VectorRecord
            {
                SessionId = sessionId,
                Text = blocks[i],
                Embedding = embeddings[i]
            });
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(_knowledgeBasePath, JsonSerializer.Serialize(records, options));

        var dimension = embeddings.First().Length;

        List<float[]> allVectors = records.Select(r => r.Embedding).ToList();

        using (var index = new IndexFlatL2(dimension))
        {
            index.Add(allVectors);
            _logger.LogInformation("Elements in memory index: {Count}", index.Count);
        }

        _logger.LogInformation("Successfully saved {Count} new vectors. Total vectors in base: {Total}", blocks.Count, records.Count);
    }

    private async Task<List<float[]>> GetEmbeddingsAsync(List<string> texts)
    {
        var request = new JsonObject
        {
            ["model"] = "text-embedding-3-small",
            ["input"] = new JsonArray(texts.Select(t => (JsonNode)t).ToArray())
        };

        var response = await _openAiApi.GetEmbeddingsAsync(request, $"Bearer {_aiOptions.OpenAiApiKey}");
        var dataArray = response["data"]?.AsArray();

        var result = new List<float[]>();
        if (dataArray == null) return result;

        foreach (var item in dataArray)
        {
            var embedding = item?["embedding"]?.AsArray().Select(v => (float)v!.GetValue<double>()).ToArray();
            if (embedding != null)
            {
                result.Add(embedding);
            }
        }

        return result;
    }
}