using System.Collections.Concurrent;

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



public class SessionManager
{
    private readonly ConcurrentDictionary<Guid, UserSession> _userSessions = new();
    private readonly ConcurrentDictionary<string, Guid> _connectionMap = new();

    public UserSession? GetSession(Guid userId)
    {
        _userSessions.TryGetValue(userId, out var session);
        return session;
    }

    public UserSession? GetSessionByConnectionId(string connectionId)
    {
        if (_connectionMap.TryGetValue(connectionId, out var userId))
        {
            return GetSession(userId);
        }
        return null;
    }

    public UserSession CreateOrGetSession(Guid userId, string connectionId)
    {
        var session = _userSessions.GetOrAdd(userId, _ => new UserSession(userId, connectionId));
        _connectionMap[connectionId] = userId;
        return session;
    }

    public void RemoveSession(string connectionId)
    {
        if (_connectionMap.TryRemove(connectionId, out var userId))
        {
            _userSessions.TryRemove(userId, out _);
        }
    }
}