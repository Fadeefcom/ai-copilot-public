using System.Text;
using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Services;

namespace CopilotBackend.ApiService.Extensions;

public static class ChatMessageExtensions
{
    public static string ToFormattedTranscriptAsync(this List<ChatMessage> prompt, ChatRole role)
    {
        var sb = new StringBuilder();

        foreach (var msg in prompt.Where(c => c.Role == role))
        {
            sb.AppendLine(msg.Content);
        }

        return sb.ToString().TrimEnd();
    }
}