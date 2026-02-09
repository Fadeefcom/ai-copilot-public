using CopilotBackend.ApiService.Abstractions;
using System.Collections.Concurrent;

namespace CopilotBackend.ApiService.Workers;

public record MemoryItem(Guid UserId, string Text);

public class BackgroundStackWorker : BackgroundService
{
    private readonly ILogger<BackgroundStackWorker> _logger;
    private readonly ConcurrentStack<MemoryItem> _stack = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ILlmProvider _lmProvider;
    private readonly IVectorDbService _vectorDbService;

    public BackgroundStackWorker(ILogger<BackgroundStackWorker> logger, ILlmProvider llmProvider, IVectorDbService vectorDbService)
    {
        _logger = logger;
        _lmProvider = llmProvider;
        _vectorDbService = vectorDbService;
    }

    public void Push(Guid userId, string data)
    {
        _stack.Push(new MemoryItem(userId, data));
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackgroundStackWorker started.");

        try
        {
            await _vectorDbService.EnsureCollectionExistsAsync("copilot-memory", 3072, stoppingToken);
            _logger.LogInformation("Azure Search Index initialization completed.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize Azure Search Index during startup.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(stoppingToken);
                if (_stack.TryPop(out var item))
                {
                    await ProcessItemAsync(item, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing stack item");
            }
        }

        _logger.LogInformation("BackgroundStackWorker stopped.");
    }

    private async Task ProcessItemAsync(MemoryItem item, CancellationToken ct)
    {
        List<string> summary = await _lmProvider.SummarizeTextAsync(item.Text, ct);
        var embeddings = await _lmProvider.GetEmbeddingAsync(summary);
        await _vectorDbService.SavePointsAsync("index", embeddings, item.UserId, ct);
    }
}