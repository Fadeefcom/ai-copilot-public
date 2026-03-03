using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CopilotBackend.ApiService.Routes;

public static class RouteExtensions
{
    public static void MapApiRoutes(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("", () => Results.Ok("healthy"));

        api.MapPost("/message", async (HttpContext ctx, [FromBody] MessageRequest req, [FromServices] AiOrchestrator orchestrator) =>
        {
            await HandleSseStream(ctx, orchestrator.StreamSmartActionAsync(
                AiOrchestrator.AiActionType.System,
                req.Model,
                req.ConnectionId,
                req.Image,
                req.Text));
        });

        api.MapPost("/assist", async (HttpContext ctx, [FromBody] AiRequest req, [FromServices] AiOrchestrator orchestrator) =>
        {
            await HandleSseStream(ctx, orchestrator.StreamSmartActionAsync(
                AiOrchestrator.AiActionType.Assist,
                req.Model,
                req.ConnectionId,
                req.Image));
        });

        api.MapPost("/followup", async (HttpContext ctx, [FromBody] AiRequest req, [FromServices] AiOrchestrator orchestrator) =>
        {
            await HandleSseStream(ctx, orchestrator.StreamSmartActionAsync(
                AiOrchestrator.AiActionType.Followup,
                req.Model,
                req.ConnectionId,
                req.Image));
        });

        api.MapPost("/audio/start", async ([FromServices] DeepgramAudioService svc, [FromQuery] string connectionId, [FromQuery] string language = "ru") =>
        {
            if (string.IsNullOrEmpty(connectionId)) return Results.BadRequest("ConnectionId is required");

            await svc.StartAsync(connectionId, language);
            return Results.Ok(new { status = "started", connectionId });
        });

        api.MapPost("/audio/stop", async ([FromServices] DeepgramAudioService svc, [FromQuery] string connectionId) =>
        {
            if (string.IsNullOrEmpty(connectionId)) return Results.BadRequest("ConnectionId is required");

            await svc.StopAsync(connectionId);
            return Results.Ok(new { status = "stopped" });
        });
    }

    private static async Task HandleSseStream(HttpContext ctx, IAsyncEnumerable<string> stream)
    {
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");
        ctx.Response.Headers.Append("Cache-Control", "no-cache");
        ctx.Response.Headers.Append("Connection", "keep-alive");

        try
        {
            await foreach (var chunk in stream)
            {
                var escapedChunk = chunk.Replace("\n", "\\n");
                await ctx.Response.WriteAsync($"data: {escapedChunk}\n\n");
                await ctx.Response.Body.FlushAsync();
            }

            await ctx.Response.WriteAsync("data: [DONE]\n\n");
            await ctx.Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            await ctx.Response.WriteAsync($"data: System: Error - {ex.Message}\n\n");
            await ctx.Response.Body.FlushAsync();
        }
    }

    public record MessageRequest(string ConnectionId, string Text, string Model, string? Image);
    public record AiRequest(string ConnectionId, string Model, string? Image);
}