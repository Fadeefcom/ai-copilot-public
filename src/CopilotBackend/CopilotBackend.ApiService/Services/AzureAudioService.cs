using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;

namespace CopilotBackend.ApiService.Services;

public class AzureAudioService : IAudioTranscriptionService
{
    private readonly AiOptions _options;
    private readonly ILogger<AzureAudioService> _logger;
    private CancellationTokenSource? _cts;

    private readonly Dictionary<SpeakerRole, AzureStreamer> _streamers = new();

    public bool IsRunning => _streamers.Any(s => s.Value.IsConnected);

    public AzureAudioService(
        IOptions<AiOptions> options,
        ILogger<AzureAudioService> logger)
    {
        _options = options.Value;
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
                var streamer = new AzureStreamer(_options, _logger, session, role);
                await streamer.ConnectAsync(language);
                _streamers[role] = streamer;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Azure audio service");
            await StopAsync();
            throw;
        }
    }

    public Task PushAudio(SpeakerRole role, byte[] data)
    {
        if (_streamers.TryGetValue(role, out var streamer))
        {
            _ = streamer.SendAudioAsync(data);
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
                await streamer.StopAsync();
                streamer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Graceful stop of Azure streamer failed: {Message}", ex.Message);
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

    private class AzureStreamer : IDisposable
    {
        private readonly ClientWebSocket _ws = new();
        private readonly AiOptions _options;
        private readonly ILogger _logger;
        private readonly SpeakerRole _role;
        private readonly UserSession _ctx;
        private readonly CancellationTokenSource _cts = new();

        public bool IsConnected => _ws.State == WebSocketState.Open;

        public AzureStreamer(AiOptions options, ILogger logger, UserSession ctx, SpeakerRole role)
        {
            _options = options;
            _logger = logger;
            _ctx = ctx;
            _role = role;
        }

        public async Task ConnectAsync(string language)
        {
            try
            {
                var deployment = _options.AudioDeployment;
                var host = new Uri(deployment.Endpoint);

                _ws.Options.SetRequestHeader("api-key", _options.ApiKey);
                _ws.Options.SetRequestHeader("openai-beta", "realtime=v1");

                await _ws.ConnectAsync(host, _cts.Token);

                var systemInstructions = string.IsNullOrWhiteSpace(language)
                    ? "System instruction."
                    : $"System instruction. The user is speaking in {language}.";

                var sessionConfig = new
                {
                    type = "session.update",
                    session = new
                    {
                        modalities = new[] { "text" },
                        instructions = systemInstructions,
                        input_audio_format = "pcm16",
                        input_audio_transcription = new
                        {
                            model = deployment.Name
                        },
                        turn_detection = new
                        {
                            type = "server_vad",
                            threshold = 0.5,
                            silence_duration_ms = 400
                        }
                    }
                };

                await SendJsonAsync(sessionConfig);
                _ = ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Azure Realtime API");
                throw;
            }
        }

        public async Task SendAudioAsync(byte[] pcmData)
        {
            if (!IsConnected) return;

            var base64Audio = Convert.ToBase64String(pcmData);
            var eventData = new
            {
                type = "input_audio_buffer.append",
                audio = base64Audio
            };

            await SendJsonAsync(eventData);
        }

        private async Task SendJsonAsync(object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending JSON to Azure");
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[1024 * 4];
            try
            {
                while (IsConnected && !_cts.Token.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var messageBuilder = new StringBuilder();
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    while (!result.EndOfMessage)
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }

                    HandleEvent(messageBuilder.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in receive loop");
            }
        }

        private void HandleEvent(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp)) return;

                if (typeProp.GetString() == "conversation.item.input_audio_transcription.completed")
                {
                    if (root.TryGetProperty("transcript", out var transcriptProp))
                    {
                        var text = transcriptProp.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            //_logger.LogInformation($"[Azure-STT] {_role}: {text}");
                            _ctx.AddMessage(_role, text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Azure event");
            }
        }

        public async Task StopAsync()
        {
            _cts.Cancel();
            if (IsConnected)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopped", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing WebSocket");
                }
            }
        }

        public void Dispose()
        {
            _cts.Dispose();
            _ws.Dispose();
        }
    }
}