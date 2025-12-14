using CopilotBackend.ApiService.Abstractions;
using System.Text;

namespace CopilotBackend.ApiService.Services;

public class ContextManager
{
    private readonly ConversationContextService _contextService;
    private readonly IContextCompressor _compressor;

    private const int ArchivalThreshold = 30;
    private const int MessagesToKeepAfterCompaction = 10;

    public ContextManager(
        ConversationContextService contextService,
        IContextCompressor compressor)
    {
        _contextService = contextService;
        _compressor = compressor;
    }

    public async Task CheckAndArchiveContextAsync()
    {
        var messages = _contextService.GetMessages().ToList();

        if (messages.Count < ArchivalThreshold)
        {
            return;
        }

        var messagesToArchive = messages
            .Take(messages.Count - MessagesToKeepAfterCompaction)
            .ToList();

        if (messagesToArchive.Count == 0) return;

        var fullTranscriptToSummarize = BuildTranscript(messagesToArchive);

        var summary = await _compressor.SummarizeContextAsync(fullTranscriptToSummarize);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            _contextService.ArchiveContext(summary);
            _contextService.CompactHistory(MessagesToKeepAfterCompaction);
        }
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