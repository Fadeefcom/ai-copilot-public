using System.Text;
using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Routes;
using CopilotBackend.ApiService.Services;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace CopilotBackend.ApiService;

public class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var backendUrl = Environment.GetEnvironmentVariable("BACKEND_URL") ?? "http://localhost:57875";
        
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddLogging();
        builder.Services.AddOpenApi();
        
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<DeepgramAudioService>();
        builder.Services.AddSingleton<IAiService, OpenAiService>();
        builder.Services.AddSingleton<IAiService, GrokService>();
        builder.Services.AddSingleton<ConversationContextService>();
        builder.Services.AddTransient<IContextCompressor, LocalLlmCompressor>();
        builder.Services.AddTransient<ContextManager>();
        builder.Services.AddSingleton<DeepgramAudioService>();
        builder.Services.AddTransient<OpenAiService>();

        var app = builder.Build();        
        
        app.Urls.Clear();
        app.Urls.Add(backendUrl);
        
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapRoutesGroup();

        app.Run();
    }
}