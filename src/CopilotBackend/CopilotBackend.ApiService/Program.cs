using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using CopilotBackend.ApiService.Routes;
using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Ai;
using CopilotBackend.ApiService.Services.Ai.Providers;
using CopilotBackend.ApiService.Services.Hubs;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Refit;
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

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll",
                policy =>
                {
                    policy.SetIsOriginAllowed(_ => true)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                });
        });

        // Configuration
        builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
        builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));

        // Core Services
        builder.Services.AddLogging();
        builder.Services.AddOpenApi();
        builder.Services.AddHttpClient();
        builder.Services.AddSignalR();

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

        builder.Services.AddRefitClient<IGrokApi>()
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri("https://api.x.ai/v1");
                c.Timeout = TimeSpan.FromMinutes(5);
            });

        builder.Services.AddRefitClient<IOpenAiApi>()
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri("https://api.openai.com/v1");
                c.Timeout = TimeSpan.FromMinutes(1);
            });

        var app = builder.Build();

        app.Urls.Clear();
        app.Urls.Add(backendUrl);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapApiRoutes();
        app.UseCors("AllowAll");
        app.MapHub<SmartHub>("/hubs/smart");

        app.Run();
    }
}