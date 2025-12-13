using CopilotBackend.ApiService.Abstractions;
using System.Text;

namespace CopilotBackend.ApiService.Services.Ai;

public class PromptManager
{
    private readonly ConversationContextService _contextService;
    private readonly string _promptsFolder;
    private readonly string _userContextFile = "user.md";
    private readonly string _systemPromptFile = "system.md";

    public PromptManager(ConversationContextService contextService, IWebHostEnvironment env)
    {
        _contextService = contextService;
        _promptsFolder = Path.Combine(env.ContentRootPath, "promts");
    }

    public async Task<List<ChatMessage>> BuildRequestMessagesAsync(string userInstruction)
    {
        var systemPrompt = await LoadPromptAsync(_systemPromptFile);
        var userPersona = await LoadPromptAsync(_userContextFile);
        var dialogueHistory = _contextService.GetFormattedLog();

        var fullSystemMessage = new StringBuilder()
            .AppendLine("--- SYSTEM INSTRUCTIONS ---")
            .AppendLine(systemPrompt)
            .AppendLine("--- USER PERSONA (ME) ---")
            .AppendLine(userPersona)
            .ToString();

        var fullUserMessage = new StringBuilder()
            .AppendLine("--- CURRENT DIALOGUE TRANSCRIPT ---")
            .AppendLine(dialogueHistory)
            .AppendLine("--- YOUR TASK ---")
            .AppendLine(userInstruction)
            .AppendLine("--- GENERATE NEXT RESPONSE FOR 'ME' NOW ---")
            .ToString();

        return new List<ChatMessage>
        {
            new(ChatRole.System, fullSystemMessage),
            new(ChatRole.User, fullUserMessage)
        };
    }

    public async Task<string> GetSystemPrompt()
    {
        var systemPrompt = await LoadPromptAsync(_systemPromptFile);
        return systemPrompt.ToString();
    }

    private async Task<string> LoadPromptAsync(string fileName)
    {
        var path = Path.Combine(_promptsFolder, fileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
    }
}