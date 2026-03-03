using System.Text;

namespace CopilotBackend.ApiService.Services;

public class ContextManager
{
    private readonly ConversationContextService _contextService;

    private const int ArchivalThreshold = 30;
    private const int MessagesToKeepAfterCompaction = 10;

    public ContextManager(
        ConversationContextService contextService)
    {
        _contextService = contextService;
    }

    public async Task CheckAndArchiveContextAsync()
    {
        return;
    }

    public IEnumerable<ConversationMessage> GetMessages()
    {
        return _contextService.GetMessages();
    }

    private static string BuildTranscript(List<ConversationMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FULL CONVERSATION LOG FOR SUMMARIZATION:");
        foreach (var msg in messages)
        {
            var label = msg.Role == SpeakerRole.Me ? "Me" : "Companion";
            sb.AppendLine($"[{label}]: {msg.Text}");
        }
        return sb.ToString();
    }
}