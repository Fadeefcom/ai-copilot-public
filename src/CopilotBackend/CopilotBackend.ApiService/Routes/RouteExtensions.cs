using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CopilotBackend.ApiService.Routes;

public static class RouteExtensions
{
    public static void MapApiRoutes(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("", () =>
        {
            return Results.Ok("healthy");
        });

        api.MapPost("/message", async ([FromBody] MessageRequest req, [FromServices] AiOrchestrator orchestrator) =>
        {
            try
            {
                var response = await orchestrator.ProcessRequestAsync(req.Model, req.Text);
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
                var response = await orchestrator.ProcessAssistRequestAsync(req.Model);
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
                var response = await orchestrator.ProcessFollowupRequestAsync(req.Model);
                return Results.Ok(new { Response = response });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        api.MapPost("/audio/start", async ([FromServices] DeepgramAudioService svc, [FromQuery] string language = "ru") =>
        {
            await svc.StartAsync(language);
            return Results.Ok(new { status = "started" });
        });

        api.MapPost("/audio/stop", async ([FromServices] DeepgramAudioService svc) =>
        {
            await svc.StopAsync();
            svc.Clear();
            return Results.Ok(new { status = "stopped" });
        });
    }

    public record MessageRequest(string Text, string Model);
    public record AiRequest(string Model);
}