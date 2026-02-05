using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using CopilotBackend.ApiService.Routes;
using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Ai;
using CopilotBackend.ApiService.Services.Ai.Providers;
using CopilotBackend.ApiService.Services.Hubs;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using Serilog.Events;
using System.Text;

namespace CopilotBackend.ApiService;

public class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/copilot-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

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

        builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

        builder.Services.AddSerilog();
        builder.Services.AddOpenApi();
        builder.Services.AddHttpClient();
        builder.Services.AddSignalR(options =>
        {
            options.AddFilter<HubErrorFilter>();
        });

        builder.Services.AddSingleton<ConversationContextService>();
        builder.Services.AddSingleton<DeepgramAudioService>();
        builder.Services.AddTransient<ContextManager>();
        builder.Services.AddTransient<IContextCompressor, AzureContextCompressor>();

        builder.Services.AddTransient<PromptManager>();
        builder.Services.AddTransient<AiOrchestrator>();
        builder.Services.AddTransient<ILlmProvider, AzureLlmProvider>();

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