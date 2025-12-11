namespace CopilotBackend.ApiService.Configuration;

public class LlmOptions
{
    public const string SectionName = "LlmSettings";
    public string LocalCompressorUrl { get; set; } = string.Empty;
    public string LocalCompressorModel { get; set; } = string.Empty;
}