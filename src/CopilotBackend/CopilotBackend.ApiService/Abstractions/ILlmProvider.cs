using CopilotBackend.ApiService.Services;

namespace CopilotBackend.ApiService.Abstractions;

public interface ILlmProvider
{
    string ProviderName { get; }

    Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, string model, string? base64Image = null, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamResponseAsync(IReadOnlyList<ChatMessage> context, string model, string? base64Image = null, CancellationToken ct = default);

    Task<List<(string, float[])>> GetEmbeddingAsync(IEnumerable<string> chanks, CancellationToken ct = default);

    Task<List<string>> SummarizeTextAsync(string text, CancellationToken ct = default);
}

public record ChatMessage(ChatRole Role, string Content);