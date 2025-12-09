using Deepgram;
using Deepgram.Models.Listen.v2.WebSocket;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Text;

namespace CopilotBackend.ApiService.Services;

// **********************************************
// ИЗМЕНЕНИЯ В DeepgramAudioService
// **********************************************

// Удалить старые классы TranscriptEntry и SlidingWindowTranscription, если они были здесь.

// Вставьте сюда классы SpeakerRole, ConversationMessage, ArchivedContext, ConversationContextService
// или убедитесь, что они доступны.

public class DeepgramAudioService : IDisposable
{
    // Заменяем старые SlidingWindowTranscription
    private readonly ConversationContextService _contextService;

    private readonly ILogger<DeepgramAudioService> _logger;
    private readonly string _deepgramApiKey;

    private ListenWebSocketClient? _micClient;
    private ListenWebSocketClient? _speakerClient;

    private WasapiCapture? _micCapture;
    private WasapiCapture? _speakerCapture;

    private LiveSchema? _micSchema;
    private LiveSchema? _speakerSchema;

    private WaveFormat _waveFormat = new WaveFormat(16000, 16, 1);

    private CancellationTokenSource? _cts;
    private bool _running;

    public bool IsRunning => _running;

    public DeepgramAudioService(
        IConfiguration config,
        ILogger<DeepgramAudioService> logger,
        ConversationContextService contextService) // <-- Добавлен
    {
        _logger = logger;
        _contextService = contextService;
        _deepgramApiKey = config["ApiKeys:DEEPGRAM_API_KEY"]
                              ?? Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY")
                              ?? throw new InvalidOperationException("Deepgram API key not configured");
    }

    public async Task StartAsync(string language = "ru")
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();

        _micClient = new ListenWebSocketClient(_deepgramApiKey);
        _speakerClient = new ListenWebSocketClient(_deepgramApiKey);

        _micSchema = new LiveSchema
        {
            Model = "nova-2",
            Language = language,
            Encoding = "linear16",
            SampleRate = 16000,
            Channels = 1,
            SmartFormat = true,
            InterimResults = false
        };

        _speakerSchema = new LiveSchema
        {
            Model = "nova-2",
            Language = language,
            Encoding = "linear16",
            SampleRate = 16000,
            Channels = 1,
            SmartFormat = true,
            InterimResults = false
        };

        await SetupDeepgramClient(_micClient, SpeakerRole.Me, language);
        await SetupDeepgramClient(_speakerClient, SpeakerRole.Companion, language);

        _ = Task.Run(() => EnsureConnectedAsync(_micClient, _micSchema!, _cts.Token));
        _ = Task.Run(() => EnsureConnectedAsync(_speakerClient, _speakerSchema!, _cts.Token));

        var enumerator = new MMDeviceEnumerator();
        var micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        var speakerDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

        if (micDevice != null)
        {
            _micCapture = new WasapiCapture(micDevice) { WaveFormat = _waveFormat };
            _micCapture.DataAvailable += (_, e) => SendToDeepgram(_micClient, e.Buffer, e.BytesRecorded);
            _micCapture.StartRecording();
        }

        if (speakerDevice != null)
        {
            _speakerCapture = new WasapiLoopbackCapture(speakerDevice) { WaveFormat = _waveFormat };
            _speakerCapture.DataAvailable += (_, e) => SendToDeepgram(_speakerClient, e.Buffer, e.BytesRecorded);
            _speakerCapture.StartRecording();
        }

        _logger.LogInformation("Dual capture started via NAudio WASAPI.");
    }

    private async Task EnsureConnectedAsync(ListenWebSocketClient client, LiveSchema schema, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected())
                {
                    await client.Connect(schema);
                    _logger.LogInformation("WebSocket reconnected.");
                }
                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect failed, retrying...");
            }
        }
    }

    // Изменен: принимает SpeakerRole
    private async Task SetupDeepgramClient(ListenWebSocketClient client, SpeakerRole role, string language)
    {
        await client.Subscribe((_, e) =>
        {
            if (e.Channel?.Alternatives != null && e.Channel.Alternatives.Count > 0)
            {
                var transcript = e.Channel.Alternatives[0].Transcript;
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    // Сохраняем в единый сервис контекста
                    _contextService.AddMessage(role, transcript);

                    var labelPrefix = role == SpeakerRole.Me ? "[I am]" : "[Companion]";
                    _logger.LogInformation($"{labelPrefix} - {transcript}");
                }
            }
        });

        var liveSchema = new LiveSchema()
        {
            Model = "nova-2",
            Language = language,
            Encoding = "linear16",
            SampleRate = 16000,
            Channels = 1,
            SmartFormat = true,
            InterimResults = false
        };

        await client.Connect(liveSchema);
    }

    private void SendToDeepgram(ListenWebSocketClient? client, byte[] buffer, int bytes)
    {
        if (client == null || bytes <= 0) return;
        var actualData = new byte[bytes];
        Array.Copy(buffer, actualData, bytes);
        client.SendBinary(actualData);
    }

    public async Task StopAsync()
    {
        if (!_running) return;
        _running = false;

        _cts?.Cancel();

        _micCapture?.StopRecording();
        _micCapture?.Dispose();
        _speakerCapture?.StopRecording();
        _speakerCapture?.Dispose();

        if (_micClient != null) await _micClient.Stop();
        if (_speakerClient != null) await _speakerClient.Stop();

        _micClient?.Dispose();
        _speakerClient?.Dispose();

        _logger.LogInformation("Dual capture stopped.");
    }

    public void Clear()
    {
        _contextService.Clear();
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _cts?.Dispose();
    }
}