using CopilotBackend.ApiService.Abstractions;
using System.Text;

namespace CopilotBackend.ApiService.Services.Ai;

public class PromptManager
{
    private readonly string _promptsFolder;
    private readonly string _userContextFile = "user.md";
    private readonly string _systemPromptFile = "system.md";
    private readonly string _assistPromptFile = "assist.md";
    private readonly string _followupPromptFile = "followup.md";
    private readonly string _whatToSayPromtFile = "whatsay.md";

    public PromptManager(IWebHostEnvironment env)
    {
        _promptsFolder = Path.Combine(env.ContentRootPath, "promts");
    }

    public async Task<List<ChatMessage>> BuildAssistMessagesAsync(UserSession session, string? retrievedContext = null)
    {
        var systemPrompt = await LoadPromptAsync(_assistPromptFile);
        var userPersona = await LoadPromptAsync(_userContextFile);
        var dialogueHistory = session.GetFormattedLog();
        var ifImage = !string.IsNullOrEmpty(session.LatestScreenshot);

        var fullSystemMessage = new StringBuilder()
            .AppendLine("--- SYSTEM INSTRUCTIONS ---")
            .AppendLine(systemPrompt);

        if (!string.IsNullOrWhiteSpace(retrievedContext))
        {
            fullSystemMessage.AppendLine()
                .AppendLine("--- RELEVANT MEMORY / CONTEXT FROM DATABASE ---")
                .AppendLine(retrievedContext)
                .AppendLine("-----------------------------------------------");
        }

        if (ifImage)
            fullSystemMessage
                .AppendLine("--- VISION TASK: CODE IDENTIFICATION AND SOLUTION ---")
                .AppendLine("1. Analyze the syntax in the image to strictly identify the programming language.")
                .AppendLine("2. Look for language-specific indicators.")
                .AppendLine("3. Fix OCR-induced errors.")
                .AppendLine("4. Provide a complete solution written ONLY in the SAME language as identified in the image.")
                .AppendLine("5. Output ONLY the source code.");

        var systemMessage = fullSystemMessage
            .AppendLine("--- USER PERSONA (ME) ---")
            .AppendLine(userPersona)
            .ToString();

        var contextMessage = new StringBuilder()
            .AppendLine("--- DIALOGUE TRANSCRIPT ---")
            .AppendLine(dialogueHistory)
            .AppendLine("--- TASK: SUGGEST NEXT RESPONSE FOR 'ME' ---")
            .ToString();

        return new List<ChatMessage>
        {
            new(ChatRole.System, systemMessage),
            new(ChatRole.User, contextMessage)
        };
    }

    public async Task<List<ChatMessage>> BuildFollowupMessagesAsync(UserSession session, string? retrievedContext = null)
    {
        var systemPrompt = await LoadPromptAsync(_followupPromptFile);
        var dialogueHistory = session.GetFormattedLog();
        var ifImage = !string.IsNullOrEmpty(session.LatestScreenshot);

        var fullSystemMessage = new StringBuilder()
            .AppendLine("--- SYSTEM INSTRUCTIONS ---")
            .AppendLine(systemPrompt);

        if (!string.IsNullOrWhiteSpace(retrievedContext))
        {
            fullSystemMessage.AppendLine()
                .AppendLine("--- RELEVANT MEMORY / CONTEXT FROM DATABASE ---")
                .AppendLine(retrievedContext)
                .AppendLine("-----------------------------------------------");
        }

        if (ifImage)
                fullSystemMessage
                    .AppendLine("--- VISION TASK: CODE IDENTIFICATION AND SOLUTION ---")
                    .AppendLine("1. Analyze the syntax in the image to strictly identify the programming language.")
                    .AppendLine("2. Look for language-specific indicators.")
                    .AppendLine("3. Fix OCR-induced errors.")
                    .AppendLine("4. Provide a complete solution written ONLY in the SAME language as identified in the image.")
                    .AppendLine("5. Output ONLY the source code.");

        var systemMessage = fullSystemMessage.ToString();

        var contextMessage = new StringBuilder()
            .AppendLine("--- DIALOGUE TRANSCRIPT ---")
            .AppendLine(dialogueHistory)
            .AppendLine("--- TASK: GENERATE 5 FOLLOW-UP QUESTIONS ---")
            .ToString();

        return new List<ChatMessage>
        {
            new(ChatRole.System, systemMessage),
            new(ChatRole.User, contextMessage)
        };
    }

    public async Task<List<ChatMessage>> BuildWhatToSay(UserSession session, string? retrievedContext = null)
    {
        var systemPrompt = await LoadPromptAsync(_whatToSayPromtFile);
        var dialogueHistory = session.GetFormattedLog();
        var ifImage = !string.IsNullOrEmpty(session.LatestScreenshot);

        var fullSystemMessage = new StringBuilder()
            .AppendLine("--- SYSTEM INSTRUCTIONS ---")
            .AppendLine(systemPrompt);

        if (!string.IsNullOrWhiteSpace(retrievedContext))
        {
            fullSystemMessage.AppendLine()
                .AppendLine("--- RELEVANT MEMORY / CONTEXT FROM DATABASE ---")
                .AppendLine(retrievedContext)
                .AppendLine("-----------------------------------------------");
        }

        if (ifImage)
            fullSystemMessage
                .AppendLine("--- VISION TASK: CODE IDENTIFICATION AND SOLUTION ---")
                .AppendLine("1. Analyze the syntax in the image to strictly identify the programming language.")
                .AppendLine("2. Look for language-specific indicators.")
                .AppendLine("3. Fix OCR-induced errors.")
                .AppendLine("4. Provide a complete solution written ONLY in the SAME language as identified in the image.")
                .AppendLine("5. Output ONLY the source code.");

        var systemMessage = fullSystemMessage.ToString();

        var contextMessage = new StringBuilder()
            .AppendLine("--- DIALOGUE TRANSCRIPT ---")
            .AppendLine(dialogueHistory)
            .AppendLine("--- TASK: GENERATE 5 FOLLOW-UP QUESTIONS ---")
            .ToString();

        return new List<ChatMessage>
        {
            new(ChatRole.System, systemMessage),
            new(ChatRole.User, contextMessage)
        };
    }

    public async Task<List<ChatMessage>> BuildRequestMessagesAsync(UserSession session, string userInstruction, string? retrievedContext = null)
    {
        var userPersona = await LoadPromptAsync(_userContextFile);
        var dialogueHistory = session.GetFormattedLog();
        var ifImage = !string.IsNullOrEmpty(session.LatestScreenshot);

        var fullSystemMessage = new StringBuilder()
            .AppendLine("--- SYSTEM INSTRUCTIONS ---")
            .AppendLine("You are a helpful assistant. Answer the question based on the provided context.");

        if (!string.IsNullOrWhiteSpace(retrievedContext))
        {
            fullSystemMessage.AppendLine()
                .AppendLine("--- RELEVANT MEMORY / CONTEXT FROM DATABASE ---")
                .AppendLine(retrievedContext)
                .AppendLine("-----------------------------------------------");
        }

        if (ifImage)
            fullSystemMessage
                .AppendLine("--- VISION TASK: CODE IDENTIFICATION AND SOLUTION ---")
                .AppendLine("1. Analyze the syntax in the image to strictly identify the programming language.")
                .AppendLine("2. Look for language-specific indicators.")
                .AppendLine("3. Fix OCR-induced errors.")
                .AppendLine("4. Provide a complete solution written ONLY in the SAME language as identified in the image.")
                .AppendLine("5. Output ONLY the source code.");

        var systemMessage = fullSystemMessage
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
            new(ChatRole.System, systemMessage),
            new(ChatRole.User, fullUserMessage)
        };
    }

    public async Task<string> GetSystemPrompt()
    {
        var systemPrompt = await LoadPromptAsync(_systemPromptFile);
        return systemPrompt.ToString();
    }

    public async Task<string> GetAssistPromt()
    {
        var systemPrompt = await LoadPromptAsync(_assistPromptFile);
        return systemPrompt.ToString();
    }

    public async Task<string> GetFollowupPromt()
    {
        var systemPrompt = await LoadPromptAsync(_followupPromptFile);
        return systemPrompt.ToString();
    }

    private async Task<string> LoadPromptAsync(string fileName)
    {
        var path = Path.Combine(_promptsFolder, fileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
    }
}