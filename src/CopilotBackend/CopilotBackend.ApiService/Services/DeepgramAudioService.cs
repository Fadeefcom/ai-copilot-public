using CopilotBackend.ApiService.Configuration;
using Deepgram;
using Deepgram.Models.Listen.v2.WebSocket;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Text;

namespace CopilotBackend.ApiService.Services;

public class DeepgramAudioService : IDisposable
{
    private readonly ConversationContextService _contextService;
    private readonly string _apiKey;
    private readonly ILogger<DeepgramAudioService> _logger;
    private CancellationTokenSource? _cts;

    private readonly StringBuilder _companionBuffer = new();
    private readonly object _bufferLock = new();

    private readonly StringBuilder _smartModeBuffer = new();
    private readonly object _smartModeLock = new();

    private readonly Dictionary<SpeakerRole, AudioStreamer> _streamers = new();

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

    public async Task StartAsync(string language, string connectionId)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();

        try
        {
            var roles = new[] { SpeakerRole.Me, SpeakerRole.Companion };
            foreach (var role in roles)
            {
                var streamer = new AudioStreamer(_apiKey, _logger, _contextService, role, OnMessageReceived);
                await streamer.ConnectAsync(language, connectionId);
                _streamers[role] = streamer;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio proxy service");
            await StopAsync(connectionId);
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

    public async Task StopAsync(string connectionId)
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

    private void OnMessageReceived(SpeakerRole role, string text)
    {
        if (role == SpeakerRole.Companion)
        {
            lock (_smartModeLock)
            {
                if (_smartModeBuffer.Length > 0) _smartModeBuffer.Append(" ");
                _smartModeBuffer.Append(text);
            }

            lock (_bufferLock)
            {
                if (_companionBuffer.Length > 0) _companionBuffer.Append(" ");
                _companionBuffer.Append(text);
            }
        }
    }

    public string PopNewText()
    {
        lock (_smartModeLock)
        {
            if (_smartModeBuffer.Length == 0) return string.Empty;
            var text = _smartModeBuffer.ToString().Trim();
            _smartModeBuffer.Clear();
            return text;
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

    public void Dispose()
    {
        _cts?.Dispose();
    }

    private class AudioStreamer : IDisposable
    {
        private ListenWebSocketClient _client;
        private readonly ConversationContextService _ctx;
        private readonly Action<SpeakerRole, string> _onMessage;
        private readonly ILogger _logger;
        private readonly SpeakerRole _role;
        private readonly string _apiKey;

        private WasapiCapture? _capture;
        private string _language = "ru";
        private string _connectionId = string.Empty;
        private bool _isReconnecting;
        private bool _isDisposed;

        public AudioStreamer(string apiKey, ILogger logger, ConversationContextService ctx, SpeakerRole role, Action<SpeakerRole, string> onMessage)
        {
            _apiKey = apiKey;
            _ctx = ctx;
            _logger = logger;
            _role = role;
            _onMessage = onMessage;
            _client = new ListenWebSocketClient(_apiKey);
        }

        public async Task ConnectAsync(string language, string connectionId)
        {
            _language = language;
            _connectionId = connectionId;

            await ConnectWebSocketAsync();
            StartLocalCapture();
        }

        private async Task ConnectWebSocketAsync()
        {
            await _client.Subscribe((_, e) =>
            {
                var transcript = e.Channel?.Alternatives?.FirstOrDefault()?.Transcript;
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    _logger.LogInformation($"[Speech-to-Text] {_role}: {transcript}");
                    _ctx.AddMessage(_connectionId, _role, transcript);
                    _onMessage(_role, transcript);
                }
            });

            var schema = new LiveSchema
            {
                Model = "nova-3",
                Language = _language,
                Encoding = "linear16",
                SampleRate = 16000,
                Channels = 1,
                SmartFormat = true,
                InterimResults = false,
                EndPointing = "100"
            };

            await _client.Connect(schema);
        }

        private void StartLocalCapture()
        {
            if (_capture != null) return; // Захват уже запущен

            var dataFlow = _role == SpeakerRole.Me ? DataFlow.Capture : DataFlow.Render;
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
                        SendAudio(buffer);
                    }
                };

                _capture.StartRecording();
            }
        }

        public void SendAudio(byte[] buffer)
        {
            if (_isDisposed || _isReconnecting) return;

            try
            {
                _client.SendBinary(buffer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Deepgram] {_role} socket error: {ex.Message}. Reconnecting...");
                _ = ReconnectAsync();
            }
        }

        private async Task ReconnectAsync()
        {
            if (_isReconnecting || _isDisposed) return;
            _isReconnecting = true;

            try
            {
                try { await _client.Stop(); } catch { }
                _client.Dispose();

                _client = new ListenWebSocketClient(_apiKey);
                await ConnectWebSocketAsync();

                _logger.LogInformation($"[Deepgram] {_role} reconnected successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Deepgram] {_role} reconnect failed.");
            }
            finally
            {
                _isReconnecting = false;
            }
        }

        public async Task StopAsync()
        {
            _isDisposed = true;
            _capture?.StopRecording();
            try { await _client.Stop(); } catch { }
        }

        public void Dispose()
        {
            _isDisposed = true;
            _capture?.Dispose();
            _client?.Dispose();
        }
    }
}