using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using CopilotBackend.ApiService.Routes;
using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Ai;
using CopilotBackend.ApiService.Services.Ai.Providers;
using Microsoft.AspNetCore.Mvc.Abstractions;
using System.Text;

namespace CopilotBackend.ApiService;

public class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var backendUrl = Environment.GetEnvironmentVariable("BACKEND_URL") ?? "http://localhost:57875";
        
        var builder = WebApplication.CreateBuilder(args);

        using CopilotBackend.ApiService.Abstractions;
        using CopilotBackend.ApiService.Configuration;
        using CopilotBackend.ApiService.Routes;
        using CopilotBackend.ApiService.Services;
        using CopilotBackend.ApiService.Services.Ai;
        using CopilotBackend.ApiService.Services.Ai.Providers;

        var builder = WebApplication.CreateBuilder(args);

        // Configuration
        builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
        builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));

        // Core Services
        builder.Services.AddServiceDefaults();
        builder.Services.AddLogging();
        builder.Services.AddOpenApi();
        builder.Services.AddHttpClient();

        // Domain Services
        builder.Services.AddSingleton<ConversationContextService>();
        builder.Services.AddSingleton<DeepgramAudioService>();
        builder.Services.AddTransient<ContextManager>();
        builder.Services.AddTransient<IContextCompressor, LocalLlmCompressor>();

        // AI Stack
        builder.Services.AddTransient<PromptManager>();
        builder.Services.AddTransient<AiOrchestrator>();
        builder.Services.AddTransient<ILlmProvider, OpenAiProvider>();
        builder.Services.AddTransient<ILlmProvider, GrokProvider>();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapApiRoutes();

        app.Run();
    }
}