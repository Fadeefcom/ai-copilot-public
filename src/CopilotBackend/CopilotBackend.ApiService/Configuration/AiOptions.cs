namespace CopilotBackend.ApiService.Configuration;

public class AiOptions
{
    public const string SectionName = "AzureAi";

    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    public DeploymentOptions AudioDeployment { get; set; } = new();
    public DeploymentOptions VisionDeployment { get; set; } = new();
    public DeploymentOptions ReasoningDeployment { get; set; } = new();
    public DeploymentOptions FastDeployment { get; set; } = new();
    public DeploymentOptions ChatDeployment { get; set; } = new();
}

public class DeploymentOptions
{
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public class ExternalAiOptions
{
    public const string SectionName = "ApiKeys";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string GroqApiKey { get; set; } = string.Empty;
    public string DeepgramApiKey { get; set; } = string.Empty;
}