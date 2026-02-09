namespace CopilotBackend.ApiService.Abstractions;

public interface IContextCompressor
{
    Task<List<(string summary, float[] embeddings)>> SummarizeContextAsync(string fullTranscript, CancellationToken ct = default);
}
