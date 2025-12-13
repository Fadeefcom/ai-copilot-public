using Refit;
using System.Text.Json.Nodes;

namespace CopilotBackend.ApiService.Services.Ai.Providers;

public interface IOpenAiApi
{
    [Post("/chat/completions")]
    Task<JsonObject> ChatCompletionAsync([Body] JsonObject request, [Header("Authorization")] string authorization);

    [Post("/chat/completions")]
    Task<HttpResponseMessage> ChatStreamAsync([Body] JsonObject request, [Header("Authorization")] string authorization);
}
