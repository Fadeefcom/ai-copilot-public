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
    Assistant
}

public class ConversationMessage
{
    public DateTime Timestamp { get; set; }
    public SpeakerRole Role { get; set; }
    public string Text { get; set; } = "";
}

public class ConversationContextService
{
    private readonly List<ConversationMessage> _history = new();
    private readonly object _lock = new();
    private readonly object _screenshotLock = new();

    private string? _latestScreenshot;

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

    public void Clear()
    {
        lock (_lock)
        {
            _history.Clear();
        }
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
                sb.AppendLine($"[{msg.Timestamp:O}] {msg.Role}: {msg.Text}");
            }

            return sb.ToString();
        }
    }

    public IEnumerable<ConversationMessage> GetMessages()
    {
        lock (_lock) return _history.ToList();
    }

    public IEnumerable<ConversationMessage> GetMessages(SpeakerRole speakerRole, TimeSpan timeSpan)
    {
        var threshold = DateTime.UtcNow - timeSpan;
        var result = new List<ConversationMessage>();

        lock (_lock)
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                var msg = _history[i];
                if (msg.Timestamp < threshold) break;

                if (msg.Role == speakerRole)
                {
                    result.Add(msg);
                }
            }
        }

        result.Reverse();
        return result;
    }
}