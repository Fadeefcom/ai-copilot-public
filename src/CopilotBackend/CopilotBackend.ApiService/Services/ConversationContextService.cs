using System.Text;

namespace CopilotBackend.ApiService.Services;

public enum ChatRole
{
    System,
    User,
    Assistant
}

public enum SpeakerRole
{
    Me,
    Companion,
    System
}

public class ConversationMessage
{
    public DateTime Timestamp { get; set; }
    public SpeakerRole Role { get; set; }
    public string Text { get; set; } = "";
}

public class ArchivedContext
{
    public DateTime ArchiveTime { get; set; }
    public string SummaryText { get; set; } = "";
}

public class ConversationContextService
{
    private readonly List<ConversationMessage> _history = new();
    private readonly List<ArchivedContext> _archives = new();
    private readonly object _lock = new();

    private readonly TimeSpan _mergeThreshold = TimeSpan.FromSeconds(5);

    public void AddMessage(SpeakerRole role, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            var lastMessage = _history.LastOrDefault();

            if (lastMessage != null &&
                lastMessage.Role == role &&
                DateTime.UtcNow - lastMessage.Timestamp < _mergeThreshold)
            {
                lastMessage.Text += $" {text}";
                lastMessage.Timestamp = DateTime.UtcNow;
            }
            else
            {
                _history.Add(new ConversationMessage
                {
                    Timestamp = DateTime.UtcNow,
                    Role = role,
                    Text = text
                });
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _history.Clear();
            _archives.Clear();
        }
    }

    public string GetFormattedLog()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();

            foreach (var archive in _archives)
            {
                sb.AppendLine($"[ARCHIVE {archive.ArchiveTime:HH:mm:ss}]: {archive.SummaryText}");
            }

            foreach (var msg in _history)
            {
                var label = msg.Role == SpeakerRole.Me ? "Me" : "Companion";
                sb.AppendLine($"[{label} {msg.Timestamp:HH:mm:ss}]: {msg.Text}");
            }
            return sb.ToString();
        }
    }

    public IEnumerable<ConversationMessage> GetMessages()
    {
        lock (_lock) return _history.ToList();
    }

    public void ArchiveContext(string summary)
    {
        lock (_lock)
        {
            _archives.Add(new ArchivedContext
            {
                ArchiveTime = DateTime.UtcNow,
                SummaryText = summary
            });
        }
    }

    public void CompactHistory(int messagesToKeep = 5)
    {
        lock (_lock)
        {
            if (_history.Count > messagesToKeep)
            {
                _history.RemoveRange(0, _history.Count - messagesToKeep);
            }
        }
    }
}