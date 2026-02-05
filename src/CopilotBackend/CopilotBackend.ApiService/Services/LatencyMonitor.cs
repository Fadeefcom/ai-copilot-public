using System.Collections.Concurrent;

namespace CopilotBackend.ApiService.Services;

public class LatencyMonitor
{
    private readonly ConcurrentQueue<long> _samples = new();
    private const int MaxSamples = 100;

    public void Record(long milliseconds)
    {
        _samples.Enqueue(milliseconds);

        while (_samples.Count > MaxSamples)
        {
            _samples.TryDequeue(out _);
        }
    }

    public double GetAverageLatency()
    {
        if (_samples.IsEmpty) return 0;
        return _samples.Average();
    }
}