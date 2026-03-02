using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using CopilotBackend.ApiService.Abstractions;
using Microsoft.Extensions.Configuration;

namespace CopilotBackend.ApiService.Services.Data;

public class AzureSearchVectorService : IVectorDbService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly string _indexName;
    private readonly ILogger<AzureSearchVectorService> _logger;
    private readonly string _defaultCategory;

    private const string VectorProfileName = "my-vector-profile";
    private const string HnswConfigName = "my-hnsw-config";

    public AzureSearchVectorService(IConfiguration config, ILogger<AzureSearchVectorService> logger)
    {
        var endpointUrl = config["VectorDb:Endpoint"] ?? throw new ArgumentNullException("VectorDb:Endpoint");
        var apiKey = config["VectorDb:ApiKey"] ?? throw new ArgumentNullException("VectorDb:ApiKey");
        _defaultCategory = config["VectorDb:DefaultCategory"]
            ?? throw new NotImplementedException("Category missing.");

        var endpoint = new Uri(endpointUrl);
        var credential = new AzureKeyCredential(apiKey);

        _indexName = config["VectorDb:IndexName"] ?? throw new ArgumentNullException("VectorDb:IndexName");
        _logger = logger;

        _indexClient = new SearchIndexClient(endpoint, credential);
        _searchClient = new SearchClient(endpoint, _indexName, credential);
    }

    public async Task EnsureCollectionExistsAsync(string collectionName, ulong vectorSize, CancellationToken ct = default)
    {
        var targetIndex = _indexName;

        try
        {
            await _indexClient.GetIndexAsync(targetIndex, ct);
            return;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation($"Index '{targetIndex}' not found. Creating...");
        }

        var searchIndex = new SearchIndex(targetIndex)
        {
            Fields =
            {
                new SearchField("id", SearchFieldDataType.String) { IsKey = true },
                new SearchField("content", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("user_id", SearchFieldDataType.String) { IsFilterable = true },
                new SearchField("category", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = (int)vectorSize,
                    VectorSearchProfileName = VectorProfileName
                }
            },

            VectorSearch = new VectorSearch
            {
                Algorithms =
                {
                    new HnswAlgorithmConfiguration(HnswConfigName)
                },
                Profiles =
                {
                    new VectorSearchProfile(VectorProfileName, HnswConfigName)
                }
            }
        };

        await _indexClient.CreateIndexAsync(searchIndex, ct);
        _logger.LogInformation($"Index '{targetIndex}' created successfully.");
    }

    public async Task SavePointsAsync(string category, IEnumerable<(string Text, float[] Vector)> items, Guid userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            category = _defaultCategory;

        var batch = new IndexDocumentsBatch<SearchDocument>();

        foreach (var (text, vector) in items)
        {
            var doc = new SearchDocument
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["content"] = text,
                ["embedding"] = vector,
                ["user_id"] = userId,   
                ["category"] = category,
            };

            batch.Actions.Add(IndexDocumentsAction.Upload(doc));
        }

        if (batch.Actions.Any())
        {
            await _searchClient.IndexDocumentsAsync(batch, new IndexDocumentsOptions { ThrowOnAnyError = true }, ct);
            _logger.LogInformation($"Saved {batch.Actions.Count} memories to Azure AI Search.");
        }
    }

    public async Task<List<string>> SearchAsync(string category, string textQuery, float[] vector, Guid userId, int limit = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            category = _defaultCategory;

        var searchOptions = new SearchOptions
        {
            Size = limit,
            Filter = $"user_id eq '{userId}' and category eq '{category}'",
            VectorSearch = new VectorSearchOptions
            {
                Queries = {
                    new VectorizedQuery(vector)
                    {
                        KNearestNeighborsCount = limit,
                        Fields = { "embedding" }
                    }
                }
            },
        };

        SearchResults<SearchDocument> response = await _searchClient.SearchAsync<SearchDocument>(textQuery, searchOptions, ct);

        var results = new List<string>();

        await foreach (var result in response.GetResultsAsync())
        {
            if (result.Document.TryGetValue("content", out var contentObj) && contentObj is string content)
            {
                results.Add(content);
            }
        }

        return results;
    }
}