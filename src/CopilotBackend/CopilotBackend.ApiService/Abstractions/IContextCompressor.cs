namespace CopilotBackend.ApiService.Abstractions;

public interface IContextCompressor
{
    Task<(string summary, float[] embeddings)> SummarizeContextAsync(string fullTranscript, CancellationToken ct = default);
}
