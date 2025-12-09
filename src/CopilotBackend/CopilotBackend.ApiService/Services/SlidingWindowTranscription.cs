namespace CopilotBackend.ApiService.Services;

public class TranscriptEntry
{
    public DateTime Timestamp { get; set; }
    public string Text { get; set; }

    public TranscriptEntry(string text)
    {
        Timestamp = DateTime.UtcNow;
        Text = $"{Timestamp:HH:mm:ss} - {text}";
    }
}

public class SlidingWindowTranscription
{
    private readonly TimeSpan _windowSize = TimeSpan.FromMinutes(5);
    private readonly List<TranscriptEntry> _fullLog = new();

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var entry = new TranscriptEntry(text);
        _fullLog.Add(entry);
    }

    public void Clear()
    {
        _fullLog.Clear();
    }

    public IEnumerable<TranscriptEntry> GetFullLog() => _fullLog;
    public string GetWindowText() => string.Join(Environment.NewLine, _fullLog.Where(e => 
        e.Timestamp >= DateTime.UtcNow.Subtract(_windowSize)).OrderBy(e => e.Timestamp).Select(e => e.Text));
}