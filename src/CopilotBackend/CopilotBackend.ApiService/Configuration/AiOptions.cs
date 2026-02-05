namespace CopilotBackend.ApiService.Configuration;

public class AiOptions
{
    public const string SectionName = "AzureAi";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string AudioDeployment { get; set; }
    public string VisionDeployment { get; set; }
    public string ReasoningDeployment { get; set; }
    public string FastDeployment { get; set; }
}