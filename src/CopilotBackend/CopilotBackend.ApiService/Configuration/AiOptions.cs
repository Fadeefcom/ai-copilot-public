namespace CopilotBackend.ApiService.Configuration;

public class AiOptions
{
    public const string SectionName = "ApiKeys";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string GroqApiKey { get; set; } = string.Empty;
    public string DeepgramApiKey { get; set; } = string.Empty;
}