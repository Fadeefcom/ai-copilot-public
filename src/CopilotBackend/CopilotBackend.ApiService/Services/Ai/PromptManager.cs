using CopilotBackend.ApiService.Abstractions;
using System.Text;

namespace CopilotBackend.ApiService.Services.Ai;

public class PromptManager
{
    private readonly ConversationContextService _contextService;
    private readonly string _promptsFolder;
    private readonly string _userContextFile = "user.md";
    private readonly string _systemPromptFile = "system.md";
    private readonly string _assistPromptFile = "assist.md";
    private readonly string _followupPromptFile = "followup.md";
    private readonly string _continuePromptFile = "continue.md";

    public PromptManager(ConversationContextService contextService, IWebHostEnvironment env)
    {
        _contextService = contextService;
        _promptsFolder = Path.Combine(env.ContentRootPath, "promts");
    }

    public async Task<List<ChatMessage>> BuildAssistMessagesAsync(string connectionId, bool ifImage = false)
    {
        var systemPrompt = await LoadPromptAsync(_assistPromptFile);
        var userPersona = await LoadPromptAsync(_userContextFile);
        var dialogueHistory = _contextService.GetFormattedLog(connectionId, [SpeakerRole.Me, SpeakerRole.Companion]);
        var aiResponses = _contextService.GetFormattedLog(connectionId, [SpeakerRole.AI]);

        var systemBuilder = new StringBuilder().AppendLine(systemPrompt);
        if (ifImage) AppendVisionTask(systemBuilder);

        systemBuilder.AppendLine("\n### USER PERSONA (ME)").AppendLine(userPersona);

        var userBuilder = new StringBuilder()
            .AppendLine("### DIALOGUE TRANSCRIPT")
            .AppendLine(dialogueHistory);

        if (!string.IsNullOrWhiteSpace(aiResponses))
        {
            userBuilder.AppendLine("\n### PREVIOUS AI ANALYSIS (CONTEXT ONLY)").AppendLine(aiResponses);
        }

        userBuilder.AppendLine("\n### FINAL INSTRUCTION").AppendLine("Suggest next response for ME.");

        return new List<ChatMessage>
        {
            new(ChatRole.System, systemBuilder.ToString()),
            new(ChatRole.User, userBuilder.ToString())
        };
    }

    public async Task<List<ChatMessage>> BuildContinueMessagesAsync(string connectionId, bool ifImage = false)
    {
        var systemPrompt = await LoadPromptAsync(_continuePromptFile);
        var dialogueHistory = _contextService.GetFormattedLog(connectionId, [SpeakerRole.Me, SpeakerRole.Companion]);
        var aiResponses = _contextService.GetFormattedLog(connectionId, [SpeakerRole.AI]);

        var systemBuilder = new StringBuilder().AppendLine(systemPrompt);
        if (ifImage) AppendVisionTask(systemBuilder);

        var userBuilder = new StringBuilder()
            .AppendLine("### DIALOGUE TRANSCRIPT")
            .AppendLine(dialogueHistory);

        if (!string.IsNullOrWhiteSpace(aiResponses))
        {
            userBuilder.AppendLine("\n### PREVIOUS AI ANALYSIS (CONTEXT ONLY)").AppendLine(aiResponses);
        }

        userBuilder.AppendLine("\n### FINAL INSTRUCTION").AppendLine("Continue my last thoughts.");

        return new List<ChatMessage>
        {
            new(ChatRole.System, systemBuilder.ToString()),
            new(ChatRole.User, userBuilder.ToString())
        };
    }

    public async Task<List<ChatMessage>> BuildFollowupMessagesAsync(string connectionId, bool ifImage = false)
    {
        var systemPrompt = await LoadPromptAsync(_followupPromptFile);
        var dialogueHistory = _contextService.GetFormattedLog(connectionId, [SpeakerRole.Me, SpeakerRole.Companion]);
        var aiResponses = _contextService.GetFormattedLog(connectionId, [SpeakerRole.AI]);

        var systemBuilder = new StringBuilder().AppendLine(systemPrompt);
        if (ifImage) AppendVisionTask(systemBuilder);

        var userBuilder = new StringBuilder()
            .AppendLine("### INPUT DATA: DIALOGUE TRANSCRIPT")
            .AppendLine("Please analyze the following conversation history:")
            .AppendLine("--- START OF TRANSCRIPT ---")
            .AppendLine(dialogueHistory)
            .AppendLine("--- END OF TRANSCRIPT ---");

        if (!string.IsNullOrWhiteSpace(aiResponses))
        {
            userBuilder.AppendLine("\n### PREVIOUS AI ANALYSIS (CONTEXT ONLY)").AppendLine(aiResponses);
        }

        userBuilder.AppendLine("\n### FINAL INSTRUCTION")
            .AppendLine("Based on the transcript above, perform the ANALYTICAL TASK and provide the 5 High-Signal 'Stinger' questions.");

        return new List<ChatMessage>
        {
            new(ChatRole.System, systemBuilder.ToString()),
            new(ChatRole.User, userBuilder.ToString())
        };
    }

    public async Task<List<ChatMessage>> BuildRequestMessagesAsync(string connectionId, string userInstruction, bool ifImage = false)
    {
        var systemPrompt = await LoadPromptAsync(_systemPromptFile);
        var userPersona = await LoadPromptAsync(_userContextFile);
        var dialogueHistory = _contextService.GetFormattedLog(connectionId, [SpeakerRole.Me, SpeakerRole.Companion]);
        var aiResponses = _contextService.GetFormattedLog(connectionId, [SpeakerRole.AI]);

        var systemBuilder = new StringBuilder().AppendLine(systemPrompt);
        if (ifImage) AppendVisionTask(systemBuilder);

        systemBuilder.AppendLine("\n### USER PERSONA (ME)").AppendLine(userPersona);

        var userBuilder = new StringBuilder()
            .AppendLine("### CURRENT DIALOGUE TRANSCRIPT")
            .AppendLine(dialogueHistory);

        if (!string.IsNullOrWhiteSpace(aiResponses))
        {
            userBuilder.AppendLine("\n### PREVIOUS AI ANALYSIS (CONTEXT ONLY)").AppendLine(aiResponses);
        }

        userBuilder.AppendLine("\n### FINAL INSTRUCTION").AppendLine(userInstruction);

        return new List<ChatMessage>
        {
            new(ChatRole.System, systemBuilder.ToString()),
            new(ChatRole.User, userBuilder.ToString())
        };
    }

    public async Task<string> GetSystemPrompt() => await LoadPromptAsync(_systemPromptFile);
    public async Task<string> GetAssistPrompt() => await LoadPromptAsync(_assistPromptFile);
    public async Task<string> GetFollowupPrompt() => await LoadPromptAsync(_followupPromptFile);

    private void AppendVisionTask(StringBuilder sb)
    {
        sb.AppendLine("\n--- VISION TASK: CODE IDENTIFICATION AND SOLUTION ---")
          .AppendLine("1. Analyze the syntax in the image to strictly identify the programming language.")
          .AppendLine("2. Look for language-specific indicators.")
          .AppendLine("3. Fix OCR-induced errors.")
          .AppendLine("4. Provide a complete solution written ONLY in the SAME language as identified in the image.")
          .AppendLine("5. Output ONLY the source code.");
    }

    private async Task<string> LoadPromptAsync(string fileName)
    {
        var path = Path.Combine(_promptsFolder, fileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
    }
}