using CopilotBackend.ApiService.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace CopilotBackend.ApiService.Services.Hubs;

public class SmartHub : Hub
{
    private readonly AiOrchestrator _orchestrator;
    private readonly DeepgramAudioService _audioService;
    private readonly ConversationContextService _contextService;
    private readonly SummarizationFaissWorker _faissWorker;
    private readonly ILogger<SmartHub> _logger;

    private static readonly ConcurrentDictionary<string, string> _latestScreenshots = new();

    public SmartHub(AiOrchestrator orchestrator, DeepgramAudioService audioService, ILogger<SmartHub> logger, ConversationContextService conversationContext, SummarizationFaissWorker faissWorker)
    {
        _orchestrator = orchestrator;
        _audioService = audioService;
        _logger = logger;
        _contextService = conversationContext;
        _faissWorker = faissWorker;
    }

    public async Task StartAudio(string language)
    {
        try
        {
            var connectionId = Context.ConnectionId;
            await _audioService.StartAsync(language, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio");
            throw;
        }
    }

    public async Task StopAudio()
    {
        var connectionId = Context.ConnectionId;
        await _audioService.StopAsync(connectionId);

        var historyToProcess = _contextService.GetFullHistoryAndClear(connectionId);
        if (historyToProcess.Any())
        {
            _ = Task.Run(() => _faissWorker.ProcessSessionAsync(connectionId, historyToProcess));
        }
    }

    public void SendAudioChunk(string base64Chunk, string role)
    {
        if (string.IsNullOrEmpty(base64Chunk)) return;

        var chunk = Convert.FromBase64String(base64Chunk);
        var speakerRole = role.ToLower() == "me" ? SpeakerRole.Me : SpeakerRole.Companion;
        _audioService.PushAudio(speakerRole, chunk);
    }

    public void UpdateVisualContext(string base64Image) => _latestScreenshots[Context.ConnectionId] = base64Image;

    public async IAsyncEnumerable<string> SendMessage(string text, string model, string? image, [EnumeratorCancellation] CancellationToken ct)
    {
        var connectionId = Context.ConnectionId;
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.System, model, connectionId, image, text);
        var aiResponseBuffer = new StringBuilder();

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            aiResponseBuffer.Append(chunk);
            yield return chunk;
        }

        _contextService.AddAiResponse(Context.ConnectionId, aiResponseBuffer.ToString());
    }

    public async IAsyncEnumerable<string> SendAssistRequest(string model, string? image, [EnumeratorCancellation] CancellationToken ct)
    {
        var connectionId = Context.ConnectionId;
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.Assist, model, connectionId, image);
        var aiResponseBuffer = new StringBuilder();

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            aiResponseBuffer.Append(chunk);
            yield return chunk;
        }

        _contextService.AddAiResponse(Context.ConnectionId, aiResponseBuffer.ToString());
    }

    public async IAsyncEnumerable<string> SendFollowupRequest(string model, string? image, [EnumeratorCancellation] CancellationToken ct)
    {
        var connectionId = Context.ConnectionId;
        var chunks = _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.Followup, model, connectionId, image);
        var aiResponseBuffer = new StringBuilder();

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            aiResponseBuffer.Append(chunk);
            yield return chunk;
        }

        _contextService.AddAiResponse(Context.ConnectionId, aiResponseBuffer.ToString());
    }

    public async IAsyncEnumerable<string> StreamSmartMode(string modelName, [EnumeratorCancellation] CancellationToken ct)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation($"[SmartHub] Smart Mode started: {Context.ConnectionId}");
        var buffer = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var newText = _audioService.PopNewText();
            if (!string.IsNullOrWhiteSpace(newText)) buffer.Append(" ").Append(newText);

            if (buffer.Length > 20)
            {
                var detectedIssue = await _orchestrator.DetectQuestionAsync(connectionId, modelName, buffer.ToString());
                if (detectedIssue != null)
                {
                    var aiResponseBuffer = new StringBuilder();
                    yield return $"[System] Intent: {detectedIssue}";
                    _latestScreenshots.TryGetValue(Context.ConnectionId, out var img);
                    await foreach (var chunk in _orchestrator.StreamSmartActionAsync(AiOrchestrator.AiActionType.System, modelName, connectionId, img, detectedIssue).WithCancellation(ct))
                    {
                        yield return chunk;
                        aiResponseBuffer.Append(chunk);
                    }

                    _contextService.AddAiResponse(Context.ConnectionId, aiResponseBuffer.ToString());
                    aiResponseBuffer.Clear();
                    buffer.Clear();
                }
            }
            await Task.Delay(500, ct);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _latestScreenshots.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}