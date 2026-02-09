using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Ai;
using CopilotBackend.ApiService.Workers;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

namespace CopilotBackend.ApiService.Services.Hubs;

public class SmartHub : Hub
{
    private readonly AiOrchestrator _orchestrator;
    private readonly IAudioTranscriptionService _audioService;
    private readonly ILogger<SmartHub> _logger;
    private readonly SessionManager _sessionManager;
    private readonly BackgroundStackWorker _worker;

    public SmartHub(AiOrchestrator orchestrator, IAudioTranscriptionService audioService, BackgroundStackWorker worker, ILogger<SmartHub> logger, SessionManager sessionManager)
    {
        _orchestrator = orchestrator;
        _audioService = audioService;
        _logger = logger;
        _sessionManager = sessionManager;
        _worker = worker;
    }

    public async Task StartAudio(string language)
    {
        try
        {
            var session = _sessionManager.GetSessionByConnectionId(Context.ConnectionId);
            if (session == null)
                throw new Exception("Session not found");
            await _audioService.StartAsync(language, session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio");
            throw;
        }
    }

    public Task SetForceMemory(bool isEnabled)
    {
        var session = _sessionManager.GetSessionByConnectionId(Context.ConnectionId);
        if (session != null)
        {
            session.IsMemoryForceEnabled = isEnabled;
            _logger.LogInformation($"[SmartHub] Memory Force Mode set to: {isEnabled}");
        }
        return Task.CompletedTask;
    }

    public override async Task OnConnectedAsync()
    {
        var userIdStr = Context.GetHttpContext()?.Request.Query["userId"].ToString() ?? Guid.Empty.ToString();

        if (Guid.TryParse(userIdStr, out var userId))
        {
            _sessionManager.CreateOrGetSession(userId, Context.ConnectionId);
            Context.Items["UserId"] = userId;
        }

        _logger.LogInformation($"Client connected: {Context.ConnectionId}, User: {Context.Items["UserId"]}");
        await base.OnConnectedAsync();
    }

    public async Task StopAudio() => await _audioService.StopAsync();

    public void SendAudioChunk(string base64Chunk, string role)
    {
        if (string.IsNullOrEmpty(base64Chunk)) return;

        var chunk = Convert.FromBase64String(base64Chunk);
        var speakerRole = role.ToLower() == "me" ? SpeakerRole.Me : SpeakerRole.Companion;
        _audioService.PushAudio(speakerRole, chunk);
    }

    private UserSession? GetCurrentSession()
    {
        return _sessionManager.GetSessionByConnectionId(Context.ConnectionId);
    }

    public async IAsyncEnumerable<string> SendMessage(string text, string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var session = GetCurrentSession();
        if (session == null)
        {
            yield return "System: Error - Session not found.";
            yield break;
        }

        var chunks = _orchestrator.StreamSmartActionAsync(
            session,
            AiOrchestrator.AiActionType.System,
            model,
            session.LatestScreenshot,
            text,
            session.IsMemoryForceEnabled);

        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> SendWhatToSay(string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var session = GetCurrentSession();
        if (session == null)
        {
            yield return "System: Error - Session not found.";
            yield break;
        }

        var chunks = _orchestrator.StreamSmartActionAsync(
            session,
            AiOrchestrator.AiActionType.WhatToSay,
            model,
            session.LatestScreenshot,
            forceMemory: session.IsMemoryForceEnabled);

        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> SendAssistRequest(string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var session = GetCurrentSession();
        if (session == null)
        {
            yield return "System: Error - Session not found.";
            yield break;
        }

        var chunks = _orchestrator.StreamSmartActionAsync(
            session,
            AiOrchestrator.AiActionType.Assist,
            model,
            session.LatestScreenshot,
            null,
            session.IsMemoryForceEnabled);

        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> SendFollowupRequest(string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var session = GetCurrentSession();
        if (session == null)
        {
            yield return "System: Error - Session not found.";
            yield break;
        }

        var chunks = _orchestrator.StreamSmartActionAsync(
            session,
            AiOrchestrator.AiActionType.Followup,
            model,
            session.LatestScreenshot,
            null,
            session.IsMemoryForceEnabled);

        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> StreamSmartMode(string modelName, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation($"[SmartHub] Smart Mode started: {Context.ConnectionId}");

        var session = GetCurrentSession();
        if (session == null)
        {
            yield return "System: Error - Session not found.";
            yield break;
        }

        List<ConversationMessage> localBuffer = new();
        var threshold = DateTime.UtcNow.AddMinutes(-5);
        var lastCheckedText = string.Empty;

        while (!ct.IsCancellationRequested)
        {
            int firstToKeepIndex = localBuffer.FindIndex(m => m.Timestamp >= threshold);

            if (firstToKeepIndex == -1)
            {
                localBuffer.Clear();
            }
            else if (firstToKeepIndex > 0)
            {
                localBuffer.RemoveRange(0, firstToKeepIndex);
            }

            IEnumerable<ConversationMessage> newMessages;
            if (localBuffer.Count == 0)
            {
                newMessages = session.GetMessages().Where(m => m.Role == SpeakerRole.Companion && m.Timestamp >= DateTime.UtcNow.AddMinutes(-5));
            }
            else
            {
                var lastMsgTime = localBuffer.Max(m => m.Timestamp);
                var msgs = session.GetMessages();
                newMessages = msgs.Where(m => m.Role == SpeakerRole.Companion && m.Timestamp > lastMsgTime);
            }

            if (newMessages.Any())
            {
                localBuffer.AddRange(newMessages.OrderBy(m => m.Timestamp));
            }

            if (localBuffer.Any())
            {
                var currentTextStack = string.Join(" ", localBuffer.Select(m => m.Text));

                if (!string.IsNullOrWhiteSpace(currentTextStack) &&
                !string.Equals(currentTextStack, lastCheckedText, StringComparison.Ordinal))
                {
                    lastCheckedText = currentTextStack;
                    var detectedIssue = await _orchestrator.DetectQuestionAsync(session, modelName, currentTextStack);
                    if (detectedIssue != null)
                    {
                        _logger.LogInformation($"[System] Intent: {detectedIssue}");

                        await foreach (var chunk in _orchestrator.StreamSmartActionAsync(
                            session,
                            AiOrchestrator.AiActionType.System,
                            modelName,
                            session.LatestScreenshot,
                            detectedIssue,
                            session.IsMemoryForceEnabled).WithCancellation(ct))
                        {
                            yield return chunk;
                        }
                    }
                }
            }
            await Task.Delay(500, ct);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var session = GetCurrentSession();
            if (session != null)
            {
                var transcript = session.GetCompleteSessionTranscript();

                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    _logger.LogInformation($"[Session End] Sending transcript for user {session.UserId} to background worker. Length: {transcript.Length}");
                    _worker.Push(session.UserId, transcript);
                }
                else
                {
                    _logger.LogInformation("[Session End] Transcript is empty, nothing to save.");
                }

                _sessionManager.RemoveSession(Context.ConnectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OnDisconnectedAsync processing");
        }

        await base.OnDisconnectedAsync(exception);
    }
}