using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CopilotBackend.ApiService.Routes;

public static class RouteExtensions
{
    public static void MapApiRoutes(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/llm/models", (IOptions<AiOptions> options) =>
        {
            var cfg = options.Value;

            var models = new[]
            {
                new
                {
                    Id = "fast",
                    Name = cfg.FastDeployment.Name
                },
                new
                {
                    Id = "chat",
                    Name = cfg.ChatDeployment.Name
                },
                new
                {
                    Id = "thinking",
                    Name = cfg.ReasoningDeployment.Name
                }
            };

            return Results.Ok(models);
        }).WithName("GetLlmModels");

        api.MapGet("/latency", () =>
        {
            return Results.Ok("42ms");
        });

        api.MapGet("/metrics", () => Results.Ok(new { latency_ms = 42 }));

        api.MapPost("/message", async ([FromBody] MessageRequest req, [FromServices] AiOrchestrator orchestrator) =>
        {
            try
            {
                var response = await orchestrator.ProcessRequestAsync(req.Model, req.Text, req.Image);
                return Results.Ok(new { Response = response });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        api.MapPost("/assist", async ([FromBody] AiRequest req, [FromServices] AiOrchestrator orchestrator) =>
        {
            try
            {
                var response = await orchestrator.ProcessAssistRequestAsync(req.Model, req.Image);
                return Results.Ok(new { Response = response });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        api.MapPost("/followup", async ([FromBody] AiRequest req, [FromServices] AiOrchestrator orchestrator) =>
        {
            try
            {
                var response = await orchestrator.ProcessFollowupRequestAsync(req.Model, req.Image);
                return Results.Ok(new { Response = response });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        api.MapPost("/audio/start", async ([FromServices] IAudioTranscriptionService svc, [FromQuery] string language = "ru") =>
        {
            await svc.StartAsync(language);
            return Results.Ok(new { status = "started" });
        });

        api.MapPost("/audio/stop", async ([FromServices] IAudioTranscriptionService svc) =>
        {
            await svc.StopAsync();
            svc.Clear();
            return Results.Ok(new { status = "stopped" });
        });
    }

    public record MessageRequest(string Text, string Model, string? Image);
    public record AiRequest(string Model, string? Image);
}