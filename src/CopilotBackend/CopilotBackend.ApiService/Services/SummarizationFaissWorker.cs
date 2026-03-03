using CopilotBackend.ApiService.Abstractions;
using System.Text;

namespace CopilotBackend.ApiService.Services;

public class SummarizationFaissWorker
{
    private readonly IEnumerable<ILlmProvider> _providers;
    private readonly ILogger<SummarizationFaissWorker> _logger;

    public SummarizationFaissWorker(IEnumerable<ILlmProvider> providers, ILogger<SummarizationFaissWorker> logger)
    {
        _providers = providers;
        _logger = logger;
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

            await SaveToFaissLocalAsync(sessionId, semanticBlocks);

            _logger.LogInformation("Successfully processed and saved {Count} blocks to FAISS for session: {SessionId}", semanticBlocks.Count, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Summarization Worker processing.");
        }
    }

    private async Task SaveToFaissLocalAsync(string sessionId, List<string> blocks)
    {
        // TODO: Здесь логика получения Embeddings (через тот же OpenAI или локальную модель)
        // Пример псевдокода:
        // var embeddings = await _embeddingService.GetEmbeddingsAsync(blocks);
        // _faissIndex.Add(embeddings, metadata: new { Session = sessionId });

        // Логируем в файл локально (уже работает через Serilog)
        _logger.LogInformation("FAISS INSERT SIMULATION: Saved {Count} vectors.", blocks.Count);
        await Task.CompletedTask;
    }
}