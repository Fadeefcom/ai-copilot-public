using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using Deepgram;
using Deepgram.Models.Listen.v2.WebSocket;
using Microsoft.Extensions.Options;

namespace CopilotBackend.ApiService.Services;

public class DeepgramAudioService : IAudioTranscriptionService
{
    private readonly string _apiKey;
    private readonly ILogger<DeepgramAudioService> _logger;
    private CancellationTokenSource? _cts;

    private readonly Dictionary<SpeakerRole, AudioStreamer> _streamers = new();

    public bool IsRunning => _streamers.Any();

    public DeepgramAudioService(
        IOptions<ExternalAiOptions> options,
        ILogger<DeepgramAudioService> logger)
    {
        _apiKey = options.Value.DeepgramApiKey;
        _logger = logger;
    }

    public async Task StartAsync(string language, UserSession session)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();

        try
        {
            var roles = new[] { SpeakerRole.Me, SpeakerRole.Companion };
            foreach (var role in roles)
            {
                var streamer = new AudioStreamer(_apiKey, _logger, session, role);
                await streamer.ConnectAsync(language);
                _streamers[role] = streamer;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio proxy service");
            await StopAsync();
            throw;
        }
    }

    public Task PushAudio(SpeakerRole role, byte[] data)
    {
        if (_streamers.TryGetValue(role, out var streamer))
        {
            streamer.SendAudio(data);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();

        foreach (var streamer in _streamers.Values)
        {
            try
            {
                await streamer.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
                streamer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Graceful stop of Deepgram streamer failed: {Message}", ex.Message);
            }
        }

        _streamers.Clear();
        _logger.LogInformation("Audio services stopped.");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }

    private class AudioStreamer : IDisposable
    {
        private readonly ListenWebSocketClient _client;
        private readonly UserSession _ctx;
        private readonly ILogger _logger;
        private readonly SpeakerRole _role;

        public AudioStreamer(string apiKey, ILogger logger, UserSession ctx, SpeakerRole role)
        {
            _ctx = ctx;
            _client = new ListenWebSocketClient(apiKey);
            _logger = logger;
            _role = role;
        }

        public async Task ConnectAsync(string language)
        {
            await _client.Subscribe((_, e) =>
            {
                var transcript = e.Channel?.Alternatives?.FirstOrDefault()?.Transcript;
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    //_logger.LogInformation($"[Speech-to-Text] {_role}: {transcript}");
                    _ctx.AddMessage(_role, transcript);
                }
            });

            var schema = new LiveSchema
            {
                Model = "nova-2",
                Language = language,
                Encoding = "linear16",
                SampleRate = 16000,
                Channels = 1,
                SmartFormat = true,
                InterimResults = false
            };
            await _client.Connect(schema);
        }

        public void SendAudio(byte[] buffer)
        {
            _client.SendBinary(buffer);
        }

        public async Task StopAsync()
        {
            await _client.Stop();
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}