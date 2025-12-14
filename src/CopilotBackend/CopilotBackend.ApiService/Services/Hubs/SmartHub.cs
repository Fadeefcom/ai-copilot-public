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
    private readonly ILogger<SmartHub> _logger;

    private static readonly ConcurrentDictionary<string, string> _latestScreenshots = new();

    public SmartHub(AiOrchestrator orchestrator, DeepgramAudioService audioService, ILogger<SmartHub> logger)
    {
        _orchestrator = orchestrator;
        _audioService = audioService;
        _logger = logger;
    }

    public void UpdateVisualContext(string base64Image)
    {
        _latestScreenshots[Context.ConnectionId] = base64Image;
    }

    public async IAsyncEnumerable<string> StreamSmartMode(string modelName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation($"[SmartHub] Started for {Context.ConnectionId}");
        yield return "Smart Mode: Active (Voice + Vision). Listening...";

        var buffer = new StringBuilder();
        var lastCheckTime = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var newText = _audioService.PopNewText();

            if (!string.IsNullOrWhiteSpace(newText))
            {
                buffer.Append(" ").Append(newText);
            }

            var bufferLength = buffer.Length;
            var timeSinceLastCheck = DateTime.UtcNow - lastCheckTime;

            bool shouldCheck = bufferLength > 50 || (bufferLength > 20 && timeSinceLastCheck.TotalSeconds > 2.5);

            if (shouldCheck)
            {
                var currentTranscript = buffer.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(currentTranscript))
                {
                    var detectedIssue = await _orchestrator.DetectQuestionAsync(modelName, currentTranscript);

                    if (detectedIssue != null)
                    {
                        yield return $"[System] Detected intent: \"{detectedIssue}\"";

                        _latestScreenshots.TryGetValue(Context.ConnectionId, out var base64Image);

                        await foreach (var chunk in _orchestrator.StreamResponseWithVisionAsync(modelName, detectedIssue, base64Image)
                                           .WithCancellation(cancellationToken))
                        {
                            yield return chunk;
                        }

                        yield return "[System] Response complete.";

                        buffer.Clear();
                    }
                }

                lastCheckTime = DateTime.UtcNow;

                if (buffer.Length > 2000)
                {
                    buffer.Remove(0, buffer.Length - 500);
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        _latestScreenshots.TryRemove(Context.ConnectionId, out _);
    }
}