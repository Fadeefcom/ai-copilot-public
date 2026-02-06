using CopilotBackend.ApiService.Services.Hubs;
using Microsoft.AspNetCore.Mvc;

namespace CopilotBackend.ApiService.Routes;

[ApiController]
[Route("api/context")]
public class VisualContextController : ControllerBase
{
    private readonly ILogger<VisualContextController> _logger;

    public VisualContextController(ILogger<VisualContextController> logger)
    {
        _logger = logger;
    }

    [HttpPost("screenshot")]
    public IActionResult UploadScreenshot([FromBody] ScreenshotRequest request)
    {
        if (string.IsNullOrEmpty(request.ConnectionId) || string.IsNullOrEmpty(request.Base64Image))
        {
            return BadRequest();
        }

        SmartHub.UpdateScreenshotForConnection(request.ConnectionId, request.Base64Image);
        return Ok();
    }
}

public record ScreenshotRequest(string ConnectionId, string Base64Image);
