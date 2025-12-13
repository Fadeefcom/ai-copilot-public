using CopilotBackend.ApiService.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

namespace CopilotBackend.ApiService.Services.Hubs;

public class SmartHub : Hub
{
    private readonly AiOrchestrator _orchestrator;
    private readonly DeepgramAudioService _audioService;
    private readonly ILogger<SmartHub> _logger;

    public SmartHub(AiOrchestrator orchestrator, DeepgramAudioService audioService, ILogger<SmartHub> logger)
    {
        _orchestrator = orchestrator;
        _audioService = audioService;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamSmartMode(string modelName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation($"[Server] StreamSmartMode called with model: '{modelName}'");

        yield return "Smart Mode: Active. Waiting for Companion's question...";

        while (!cancellationToken.IsCancellationRequested)
        {
            var question = _audioService.GetAndClearCompleteQuestion();

            if (!string.IsNullOrWhiteSpace(question))
            {
                yield return $"[System] Question detected: \"{question}\"";

                await foreach (var chunk in _orchestrator.StreamRequestAsync(modelName, question).WithCancellation(cancellationToken))
                {
                    yield return chunk;
                }

                yield return "[System] Response complete.";
            }

            await Task.Delay(100, cancellationToken);
        }
    }
}