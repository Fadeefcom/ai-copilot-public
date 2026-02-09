using CopilotBackend.ApiService.Services;
using CopilotBackend.ApiService.Services.Hubs;
using Microsoft.AspNetCore.Mvc;

namespace CopilotBackend.ApiService.Routes;

[ApiController]
[Route("api/context")]
public class VisualContextController : ControllerBase
{
    private readonly ILogger<VisualContextController> _logger;
    private readonly ConversationContextService _conversationContextService;

    public VisualContextController(ILogger<VisualContextController> logger, ConversationContextService conversationContextService)
    {
        _logger = logger;
        _conversationContextService = conversationContextService;
    }

    [HttpPost("screenshot")]
    public IActionResult UploadScreenshot([FromBody] ScreenshotRequest request)
    {
        if (string.IsNullOrEmpty(request.ConnectionId) || string.IsNullOrEmpty(request.Base64Image))
        {
            return BadRequest();
        }

        _conversationContextService.LatestScreenshot = request.Base64Image;
        return Ok();
    }
}

public record ScreenshotRequest(string ConnectionId, string Base64Image);
