namespace CopilotBackend.ApiService.Services.Hubs;

public class SmartHub : Hub
{
    private readonly AiOrchestrator _orchestrator;
    private readonly ConversationContextService _contextService;

    public SmartHub(AiOrchestrator orchestrator, ConversationContextService contextService)
    {
        _orchestrator = orchestrator;
        _contextService = contextService;
    }

    public async Task ActivateSmartMode(string modelName)
    {
        var messages = _contextService.GetMessages();
        var lastUserMessage = messages.LastOrDefault(m => m.Role == SpeakerRole.Me);

        if (lastUserMessage != null && (DateTime.UtcNow - lastUserMessage.Timestamp).TotalMinutes < 10)
        {
            try
            {
                var response = await _orchestrator.ProcessRequestAsync(modelName, lastUserMessage.Text);
                await Clients.Caller.SendAsync("ReceiveResponse", response);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("ReceiveError", ex.Message);
            }
        }
        else
        {
            await Clients.Caller.SendAsync("ReceiveStatus", "Smart Mode: Active (No recent question found)");
        }
    }
}