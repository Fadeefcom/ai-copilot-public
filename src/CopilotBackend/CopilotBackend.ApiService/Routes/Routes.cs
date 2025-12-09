using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Services;
using Microsoft.AspNetCore.Mvc;

namespace CopilotBackend.ApiService.Routes;

public static class Routes
{
    public static void MapRoutesGroup(this WebApplication app)
    {
        app.MapPost("/message", async ([FromBody] MessageRequest req, [FromServices] IEnumerable<IAiService> aiServices) =>
        {
            var ai = aiServices.FirstOrDefault(a => a.ModelName == req.Model);
            
            if(ai == null)
                return Results.BadRequest(new { Error = $"Model {req.Model} not found" });
            
            var response = await ai.SendMessageWithContextAsync(req.Text);
            return Results.Ok(new { Response = response });
        });
        
        app.MapPost("/audio/start", async ([FromServices] DeepgramAudioService svc, [FromQuery] string language = "ru") =>
        {
            await svc.StartAsync(language);
            return Results.Ok(new { status = "started" });
        });

        app.MapPost("/audio/stop", async ([FromServices] DeepgramAudioService svc) =>
        {
            await svc.StopAsync();
            svc.Clear();
            return Results.Ok(new { status = "stopped" });
        });
    }

    public record MessageRequest(string Text, string Model);
}
