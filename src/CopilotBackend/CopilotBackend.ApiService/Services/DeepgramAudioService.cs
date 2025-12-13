using CopilotBackend.ApiService.Configuration;
using Deepgram;
using Deepgram.Models.Listen.v2.WebSocket;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace CopilotBackend.ApiService.Services;

public class DeepgramAudioService : IDisposable
{
    private readonly ConversationContextService _contextService;
    private readonly string _apiKey;
    private readonly ILogger<DeepgramAudioService> _logger;
    private CancellationTokenSource? _cts;

    private readonly StringBuilder _companionBuffer = new();
    private readonly object _bufferLock = new();

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
            var micStreamer = new AudioStreamer(_apiKey, _logger, _contextService, OnMessageReceived);
            await micStreamer.InitializeAsync(SpeakerRole.Me, language, DataFlow.Capture);
            _streamers.Add(micStreamer);

            var speakerStreamer = new AudioStreamer(_apiKey, _logger, _contextService, OnMessageReceived);
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

    private void OnMessageReceived(SpeakerRole role, string text)
    {
        if (role == SpeakerRole.Companion)
        {
            lock (_bufferLock)
            {
                if (_companionBuffer.Length > 0) _companionBuffer.Append(" ");
                _companionBuffer.Append(text);
            }
        }
    }

    public string? GetAndClearCompleteQuestion()
    {
        lock (_bufferLock)
        {
            var text = _companionBuffer.ToString();

            int questionIndex = text.IndexOf('?');

            if (questionIndex != -1)
            {
                var question = text.Substring(0, questionIndex + 1).Trim();

                _companionBuffer.Clear();

                return question;
            }
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
        private readonly Action<SpeakerRole, string> _onMessage;
        private readonly ILogger _logger;
        private WasapiCapture? _capture;
        private readonly string _apiKey;

        public AudioStreamer(string apiKey, ILogger logger, ConversationContextService ctx, Action<SpeakerRole, string> onMessage)
        {
            _apiKey = apiKey;
            _ctx = ctx;
            _client = new ListenWebSocketClient(_apiKey);
            _logger = logger;
            _onMessage = onMessage;
        }

        public async Task InitializeAsync(SpeakerRole role, string language, DataFlow dataFlow)
        {
            await _client.Subscribe((_, e) =>
            {
                var transcript = e.Channel?.Alternatives?.FirstOrDefault()?.Transcript;
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    _ctx.AddMessage(role, transcript);
                    _logger.LogInformation($"{role} - {transcript}");
                    _onMessage(role, transcript);
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

