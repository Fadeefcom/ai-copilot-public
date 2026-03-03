using System.Collections.Concurrent;
using System.Text;

namespace CopilotBackend.ApiService.Services;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Developer
}

public enum SpeakerRole
{
    Me,
    Companion,
    System, 
    AI
}

public class ConversationMessage
{
    public DateTime Timestamp { get; set; }
    public SpeakerRole Role { get; set; }
    public string Text { get; set; } = "";
}

public class UserSessionState
{
    public List<ConversationMessage> History { get; } = new();
    public object LockObj { get; } = new();
}

public class ConversationContextService
{
    private readonly ConcurrentDictionary<string, UserSessionState> _sessions = new();
    private readonly TimeSpan _mergeThreshold = TimeSpan.FromMilliseconds(200);
    private readonly ILogger<ConversationContextService> _logger;

    public ConversationContextService(ILogger<ConversationContextService> logger)
    {
        _logger = logger;
    }

    public void AddMessage(string connectionId, SpeakerRole role, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var session = GetOrCreateSession(connectionId);
        lock (session.LockObj)
        {
            var lastMessage = session.History.LastOrDefault();
            if (lastMessage != null && lastMessage.Role == role && DateTime.UtcNow - lastMessage.Timestamp < _mergeThreshold)
            {
                lastMessage.Text += $" {text}";
                lastMessage.Timestamp = DateTime.UtcNow;
            }
            else
            {
                session.History.Add(new ConversationMessage { Timestamp = DateTime.UtcNow, Role = role, Text = text });
                _logger.LogInformation("[{ConnectionId}] {Role}: {Text}", connectionId, role, text);
            }
        }
    }

    private UserSessionState GetOrCreateSession(string connectionId) =>
        _sessions.GetOrAdd(connectionId, _ => new UserSessionState());

    public void AddAiResponse(string connectionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var session = GetOrCreateSession(connectionId);
        lock (session.LockObj)
        {
            session.History.Add(new ConversationMessage { Timestamp = DateTime.UtcNow, Role = SpeakerRole.AI, Text = text });
            _logger.LogInformation("[{ConnectionId}] AI_RESPONSE: {Text}", connectionId, text);
        }
    }

    public List<ConversationMessage> GetFullHistoryAndClear(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            lock (session.LockObj) return session.History.ToList();
        }
        return new List<ConversationMessage>();
    }

    public string GetFormattedLog(string connectionId, SpeakerRole[] requiredRoles)
    {
        if (!_sessions.TryGetValue(connectionId, out var session)) return "";

        lock (session.LockObj)
        {
            var sb = new StringBuilder();
            foreach (var msg in session.History.Where(r => requiredRoles.Contains(r.Role)))
            {
                sb.AppendLine($"[{msg.Timestamp:HH:mm:ss}]: {msg.Text}");
            }
            return sb.ToString();
        }
    }
}