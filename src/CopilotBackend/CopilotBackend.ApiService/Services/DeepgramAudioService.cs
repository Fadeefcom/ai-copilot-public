using Deepgram.Models.Listen.v2.WebSocket;
using Deepgram;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using CopilotBackend.ApiService.Configuration;
using Microsoft.Extensions.Options;

namespace CopilotBackend.ApiService.Services;

public class DeepgramAudioService : IDisposable
{
    private readonly ConversationContextService _contextService;
    private readonly string _apiKey;
    private readonly ILogger<DeepgramAudioService> _logger;
    private CancellationTokenSource? _cts;

    private readonly List<AudioStreamer> _streamers = new();

    public bool IsRunning => _streamers.Any();

    public DeepgramAudioService(
        IOptions<AiOptions> options,
        ILogger<DeepgramAudioService> logger,
        ConversationContextService contextService)
    {
        _apiKey = options.Value.DeepgramApiKey;
        _logger = logger;
        _contextService = contextService;
    }

    public async Task StartAsync(string language)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();

        try
        {
            var micStreamer = new AudioStreamer(_apiKey, _logger, _contextService);
            await micStreamer.InitializeAsync(SpeakerRole.Me, language, DataFlow.Capture);
            _streamers.Add(micStreamer);

            var speakerStreamer = new AudioStreamer(_apiKey, _logger, _contextService);
            await speakerStreamer.InitializeAsync(SpeakerRole.Companion, language, DataFlow.Render);
            _streamers.Add(speakerStreamer);

            _logger.LogInformation("Audio services started.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio services");
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        foreach (var streamer in _streamers)
        {
            await streamer.StopAsync();
            streamer.Dispose();
        }
        _streamers.Clear();
        _logger.LogInformation("Audio services stopped.");
    }

    public string GetAndClearCompleteQuestion()
    {
        // Заглушка:
        var currentBuffer = _transcription.GetFullTranscriptionText();
        if (currentBuffer.EndsWith("?") || currentBuffer.Length > 100)
        {
            _transcription.Clear();
            return currentBuffer;
        }

        return null;
    }

    public void Clear() => _contextService.Clear();

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }

    protected class AudioStreamer : IDisposable
    {
        private readonly ListenWebSocketClient _client;
        private readonly ConversationContextService _ctx;
        private readonly ILogger _logger;
        private WasapiCapture? _capture;
        private readonly string _apiKey;

        public AudioStreamer(string apiKey, ILogger logger, ConversationContextService ctx)
        {
            _apiKey = apiKey;
            _ctx = ctx;
            _client = new ListenWebSocketClient(_apiKey);
            _logger = logger;
        }

        public async Task InitializeAsync(SpeakerRole role, string language, DataFlow dataFlow)
        {
            // Настройка подписки
            await _client.Subscribe((_, e) =>
            {
                var transcript = e.Channel?.Alternatives?.FirstOrDefault()?.Transcript;
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    _ctx.AddMessage(role, transcript);
                    _logger.LogInformation($"{role} - {transcript}");

                }
            });

            // Подключение
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

            // Захват аудио
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Console);

            if (device != null)
            {
                _capture = dataFlow == DataFlow.Render
                    ? new WasapiLoopbackCapture(device) { WaveFormat = new WaveFormat(16000, 16, 1) }
                    : new WasapiCapture(device) { WaveFormat = new WaveFormat(16000, 16, 1) };

                _capture.DataAvailable += (_, e) =>
                {
                    if (e.BytesRecorded > 0)
                    {
                        var buffer = new byte[e.BytesRecorded];
                        Array.Copy(e.Buffer, buffer, e.BytesRecorded);
                        _client.SendBinary(buffer);
                    }
                };
                _capture.StartRecording();
            }
        }

        public async Task StopAsync()
        {
            _capture?.StopRecording();
            await _client.Stop();
        }

        public void Dispose()
        {
            _capture?.Dispose();
            _client.Dispose();
        }
    }
}

