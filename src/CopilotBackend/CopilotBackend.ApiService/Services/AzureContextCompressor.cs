using CopilotBackend.ApiService.Abstractions;
using CopilotBackend.ApiService.Configuration;
using Microsoft.Extensions.Options;

namespace CopilotBackend.ApiService.Services;

public class AzureContextCompressor : IContextCompressor
{
    private readonly ILlmProvider _llmProvider;
    private readonly string _model;

    public AzureContextCompressor(IEnumerable<ILlmProvider> providers, IOptions<AiOptions> options)
    {
        _llmProvider = providers.First(p => p.ProviderName == "Azure");
        _model = options.Value.FastDeployment;
    }

    public async Task<string> SummarizeContextAsync(string fullTranscript)
    {
        var systemPrompt = "You are a strictly professional, ultra-concise, and purely factual assistant. " +
                           "Your task is to summarize the following conversation transcript. " +
                           "Focus exclusively on key facts and decisions. " +
                           "The summary must be brief, neutral, and delivered in a business-like style without any conversational preamble.";

        var messages = new List<ChatMessage>
    {
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, $"TRANSCRIPT:\n{fullTranscript}")
    };

        return await _llmProvider.GenerateResponseAsync(messages, _model);
    }
}