using CopilotBackend.ApiService.Configuration;
using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Ai;
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

        api.MapGet("/latency", (LatencyMonitor monitor) =>
        {
            var avg = monitor.GetAverageLatency();
            return Results.Ok(new
            {
                average_latency_ms = avg,
                formatted = $"{avg:F2}ms"
            });
        });

        api.MapGet("/metrics", () => Results.Ok(new { latency_ms = 42 }));

        api.MapPost("/message", async ([FromBody] MessageRequest req, [FromServices] AiOrchestrator orchestrator, [FromServices] SessionManager sessionManager) =>
        {
            try
            {
                var session = sessionManager.GetSession(Guid.Empty);
                if (session == null)
                    return Results.BadRequest();

                var response = await orchestrator.ProcessRequestAsync(req.Model, req.Text, session);
                return Results.Ok(new { Response = response });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        api.MapPost("/assist", async ([FromBody] AiRequest req, [FromServices] AiOrchestrator orchestrator, [FromServices] SessionManager sessionManager) =>
        {
            try
            {
                var session = sessionManager.GetSession(Guid.Empty);
                if (session == null)
                    return Results.BadRequest();

                var response = await orchestrator.ProcessAssistRequestAsync(req.Model, session);
                return Results.Ok(new { Response = response });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        api.MapPost("/followup", async ([FromBody] AiRequest req, [FromServices] AiOrchestrator orchestrator, [FromServices] SessionManager sessionManager) =>
        {
            try
            {
                var session = sessionManager.GetSession(Guid.Empty);
                if (session == null)
                    return Results.BadRequest();

                var response = await orchestrator.ProcessFollowupRequestAsync(req.Model, session);
                return Results.Ok(new { Response = response });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });
    }

    public record MessageRequest(string Text, string Model, string? Image);
    public record AiRequest(string Model, string? Image);
}