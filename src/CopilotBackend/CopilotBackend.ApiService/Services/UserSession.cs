using System.Text;

namespace CopilotBackend.ApiService.Services;

public class UserSession
{
    public Guid UserId { get; }
    public string ConnectionId { get; }
    public bool IsMemoryForceEnabled { get; set; }

    private readonly List<ConversationMessage> _history = new();
    private readonly object _lock = new();
    private readonly object _screenshotLock = new();

    private string? _latestScreenshot;

    public UserSession(Guid userId, string connectionId)
    {
        UserId = userId;
        ConnectionId = connectionId;
    }

    public string? LatestScreenshot
    {
        get
        {
            lock (_screenshotLock)
            {
                return _latestScreenshot;
            }
        }
        set
        {
            lock (_screenshotLock)
            {
                _latestScreenshot = value;
            }
        }
    }

    public void AddMessage(SpeakerRole role, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            _history.Add(new ConversationMessage
            {
                Timestamp = DateTime.UtcNow,
                Role = role,
                Text = text
            });
        }
    }

    public void AddAssistantMessage(string text)
    {
        AddMessage(SpeakerRole.Assistant, text);
    }

    public List<ConversationMessage> GetMessages()
    {
        lock (_lock) return _history.ToList();
    }

    public string GetFormattedLog()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            foreach (var msg in _history)
            {
                sb.AppendLine($"[{msg.Role} {msg.Timestamp:HH:mm:ss}]: {msg.Text}");
            }
            return sb.ToString();
        }
    }

    public string GetCompleteSessionTranscript()
    {
        lock (_lock)
        {
            if (_history.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"SESSION DUMP GENERATED AT: {DateTime.UtcNow:O}");
            sb.AppendLine("------------------------------------------------");

            foreach (var msg in _history)
            {
                var safeText = msg.Text?.Replace("\r", "").Replace("\n", " ") ?? string.Empty;
                sb.Append($"[{msg.Timestamp:O}] {msg.Role}: {safeText} | ");
            }

            return sb.ToString();
        }
    }
}