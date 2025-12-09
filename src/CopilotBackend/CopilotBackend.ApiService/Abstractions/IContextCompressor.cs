namespace CopilotBackend.ApiService.Abstractions;

public interface IContextCompressor
{
    Task<string> SummarizeContextAsync(string fullTranscript);
}
