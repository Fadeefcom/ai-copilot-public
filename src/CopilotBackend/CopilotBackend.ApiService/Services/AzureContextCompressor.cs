using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using Microsoft.Extensions.Options;

namespace CopilotBackend.ApiService.Services;

public class AzureContextCompressor : IContextCompressor
{
    private readonly ILlmProvider _llmProvider;

    public AzureContextCompressor(IEnumerable<ILlmProvider> providers, IOptions<AiOptions> options)
    {
        _llmProvider = providers.First(p => p.ProviderName == "Azure");
    }

    public async Task<(string summary, float[] embeddings)> SummarizeContextAsync(string fullTranscript, CancellationToken ct = default)
    {
        var summary = await _llmProvider.SummarizeTextAsync(fullTranscript, ct);
        var embeddings = await _llmProvider.GetEmbeddingAsync(fullTranscript, ct);

        return (summary, embeddings);
    }
}