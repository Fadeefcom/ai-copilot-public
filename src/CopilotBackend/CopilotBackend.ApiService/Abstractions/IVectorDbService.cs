namespace CopilotBackend.ApiService.Abstractions;

public interface IVectorDbService
{
    Task EnsureCollectionExistsAsync(string collectionName, ulong vectorSize, CancellationToken ct = default);

    Task SavePointsAsync(string collectionName, IEnumerable<(string Text, float[] Vector)> items, Guid userId, CancellationToken ct = default);

    Task<List<string>> SearchAsync(string category, string textQuery, float[] vector, Guid userId, int limit = 5, CancellationToken ct = default);
}