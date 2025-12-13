using System.Runtime.CompilerServices;

namespace CopilotBackend.ApiService.Abstractions;

public interface ILlmProvider
{
    string ProviderName { get; }
    Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, string model, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamResponseAsync(IReadOnlyList<ChatMessage> context, string model, CancellationToken ct = default);
}

public record ChatMessage(string Role, string Content);