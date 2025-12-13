using Refit;
using System.Text.Json.Nodes;

namespace CopilotBackend.ApiService.Services.Ai.Providers;

public interface IGrokApi
{
    [Post("/chat/completions")]
    Task<JsonObject?> ChatCompletionAsync([Body] JsonObject request, [Header("Authorization")] string authorization);

    [Post("/chat/completions")]
    [Headers("Accept: application/json")]
    Task<HttpResponseMessage> ChatStreamAsync([Body] JsonObject request, [Header("Authorization")] string authorization);
}