namespace CopilotBackend.ApiService.Abstractions;

public interface IAiService
{
    string ModelName { get; }
    
    Task<string> SendMessageWithContextAsync(string userQuestion);
}