using CopilotBackend.ApiService.Abstractions;
using System.Text;
using System.Text.Json;

namespace CopilotBackend.ApiService.Services;

public class LocalLlmCompressor : IContextCompressor
{
    private readonly HttpClient _http;
    private readonly string _localLlmApiUrl;
    private readonly string _modelName;
    private readonly ILogger<IContextCompressor> _logger;

    public LocalLlmCompressor(IConfiguration config,ILogger<IContextCompressor> logger, HttpClient http)
    {
        _http = http;
        _localLlmApiUrl = config["LlmSettings:LocalCompressorUrl"] ??
                          throw new InvalidOperationException("Local LLM URL not configured.");
        _modelName = config["LlmSettings:LocalCompressorModel"]!;
        _logger = logger;
    }

    public async Task<string> SummarizeContextAsync(string fullTranscript)
    {
        var summaryPrompt = $"Summarize the following conversation transcript concisely, focusing on key facts and decisions. The summary must be brief and neutral.\n\nTRANSCRIPT:\n{fullTranscript}";

        var requestBody = new
        {
            model = _modelName,
            prompt = summaryPrompt,
            stream = false,
            options = new { temperature = 0.2f }
        };

        var jsonPayload = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync(_localLlmApiUrl, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var responseText = doc.RootElement.GetProperty("response").GetString();

            return responseText ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(1001, ex, "Local LLM errror");
            return "";
        }
    }
}