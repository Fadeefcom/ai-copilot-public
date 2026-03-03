using CopilotBackend.ApiService.Abstractions;

public class UserSession
{
    public string ConnectionId { get; set; } = string.Empty;
    public List<ChatMessage> History { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}