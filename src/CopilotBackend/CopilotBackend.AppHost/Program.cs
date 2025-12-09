namespace CopilotBackend.AppHost;

public static class Program
{
    public static async Task Main(string[] args) 
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var app = builder.Build();
        
        builder.AddProject<Projects.CopilotBackend_ApiService>("apiservice")
            .WithExternalHttpEndpoints();

        await app.RunAsync();
    }
}