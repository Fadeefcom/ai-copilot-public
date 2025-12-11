namespace CopilotBackend.ApiService.Abstractions;

public interface ILlmProvider
{
    string ProviderName { get; }
    Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default);
}

public record ChatMessage(string Role, string Content);