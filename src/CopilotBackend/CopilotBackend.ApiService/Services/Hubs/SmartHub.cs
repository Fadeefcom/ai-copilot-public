using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace CopilotBackend.ApiService.Services.Hubs;

public class SmartHub : Hub
{
    private readonly AiOrchestrator _orchestrator;
    private readonly IAudioTranscriptionService _audioService;
    private readonly ILogger<SmartHub> _logger;

    private static readonly ConcurrentDictionary<string, string> _latestScreenshots = new();

    public SmartHub(AiOrchestrator orchestrator, IAudioTranscriptionService audioService, ILogger<SmartHub> logger)
    {
        _orchestrator = orchestrator;
        _audioService = audioService;
        _logger = logger;
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
        var effectiveImage = _latestScreenshots.GetValueOrDefault(Context.ConnectionId);
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.System, model, effectiveImage, text);
        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> SendAssistRequest(string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var effectiveImage = _latestScreenshots.GetValueOrDefault(Context.ConnectionId);
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.Assist, model, effectiveImage);
        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> SendFollowupRequest(string model, [EnumeratorCancellation] CancellationToken ct)
    {
        var effectiveImage = _latestScreenshots.GetValueOrDefault(Context.ConnectionId);
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.Followup, model, effectiveImage);
        await foreach (var chunk in chunks.WithCancellation(ct)) yield return chunk;
    }

    public async IAsyncEnumerable<string> StreamSmartMode(string modelName, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation($"[SmartHub] Smart Mode started: {Context.ConnectionId}");
        var buffer = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var newText = _audioService.PopNewText();
            if (!string.IsNullOrWhiteSpace(newText)) buffer.Append(" ").Append(newText);

            if (buffer.Length > 20)
            {
                var detectedIssue = await _orchestrator.DetectQuestionAsync(modelName, buffer.ToString());
                if (detectedIssue != null)
                {
                    _logger.LogInformation($"[System] Intent: {detectedIssue}");
                    _latestScreenshots.TryGetValue(Context.ConnectionId, out var img);
                    await foreach (var chunk in _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.System, modelName, img, detectedIssue).WithCancellation(ct))
                        yield return chunk;
                    buffer.Clear();
                }
            }
            await Task.Delay(500, ct);
        }
    }

    public static void UpdateScreenshotForConnection(string connectionId, string base64Image)
    {
        _latestScreenshots[connectionId] = base64Image;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _latestScreenshots.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}