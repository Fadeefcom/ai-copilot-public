using CopilotBackend.ApiService.Abstractions;
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
    private readonly ConversationContextService _conversationContextService;
    private readonly BackgroundStackWorker _worker;

    public SmartHub(AiOrchestrator orchestrator, IAudioTranscriptionService audioService, BackgroundStackWorker worker, ILogger<SmartHub> logger, ConversationContextService conversationContextService)
    {
        _orchestrator = orchestrator;
        _audioService = audioService;
        _logger = logger;
        _conversationContextService = conversationContextService;
        _worker = worker;
    }

    public async Task StartAudio(string language)
    {
        try
        {
            await _audioService.StartAsync(language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio");
            throw;
        }
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();
        Context.Items["UserId"] = Guid.Empty;

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

    public async IAsyncEnumerable<string> SendMessage(string text, string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var effectiveImage = _conversationContextService.LatestScreenshot;
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.System, model, effectiveImage, text);
        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> SendAssistRequest(string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var effectiveImage = _conversationContextService.LatestScreenshot;
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.Assist, model, effectiveImage);
        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> SendFollowupRequest(string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var effectiveImage = _conversationContextService.LatestScreenshot;
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.Followup, model, effectiveImage);
        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> StreamSmartMode(string modelName, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation($"[SmartHub] Smart Mode started: {Context.ConnectionId}");
        List<ConversationMessage> localBuffer = new ();
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
                newMessages = _conversationContextService.GetMessages(SpeakerRole.Companion, TimeSpan.FromMinutes(5));
            }
            else
            {
                var lastMsgTime = localBuffer.Max(m => m.Timestamp);
                var timeSinceLast = DateTime.UtcNow - lastMsgTime;

                var rawNew = _conversationContextService.GetMessages(SpeakerRole.Companion, timeSinceLast + TimeSpan.FromSeconds(1));

                newMessages = rawNew.Where(m => m.Timestamp > lastMsgTime);
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
                    var detectedIssue = await _orchestrator.DetectQuestionAsync(modelName, currentTextStack);
                    if (detectedIssue != null)
                    {
                        _logger.LogInformation($"[System] Intent: {detectedIssue}");
                        var img = _conversationContextService.LatestScreenshot;

                        await foreach (var chunk in _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.System, modelName, img, detectedIssue).WithCancellation(ct))
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
            var userId = Context.Items["UserId"]?.ToString();

            if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var formattedId))
            {
                await base.OnDisconnectedAsync(exception);
                return;
            }

            var transcript = _conversationContextService.GetCompleteSessionTranscript();

            if (!string.IsNullOrWhiteSpace(transcript))
            {
                _logger.LogInformation($"[Session End] Sending transcript for user {userId} to background worker. Length: {transcript.Length}");
                _worker.Push(formattedId, transcript);
            }
            else
            {
                _logger.LogInformation("[Session End] Transcript is empty, nothing to save.");
            }

            _conversationContextService.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OnDisconnectedAsync processing");
        }

        await base.OnDisconnectedAsync(exception);
    }
}