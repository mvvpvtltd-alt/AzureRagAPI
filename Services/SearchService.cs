using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using AzureRagAPI.Models;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Office2010.Word;
using System.Diagnostics;

namespace AzureRagAPI.Services;

public sealed class SearchService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SearchService> _logger;

    private const string IndexName = "documents-index";

    public SearchService(IConfiguration configuration, ILogger<SearchService> logger)
    {
        _configuration = configuration;
        _logger = logger; ;

        string endpoint =
            configuration["AzureSearch:Endpoint"]
            ?? throw new InvalidOperationException(
                "Azure Search endpoint is missing.");

        string apiKey =
            configuration["AzureSearch:ApiKey"]
            ?? throw new InvalidOperationException(
                "Azure Search API key is missing.");

        var credential = new AzureKeyCredential(apiKey);

        _indexClient = new SearchIndexClient(
            new Uri(endpoint),
            credential);

        _searchClient = new SearchClient(
            new Uri(endpoint),
            IndexName,
            credential);
    }

    public async Task CreateIndexIfNotExistsAsync()
    {
        var existingIndexes = new HashSet<string>(
            _indexClient.GetIndexNames());

        if (existingIndexes.Contains(IndexName))
            return;

        var fields = new List<SearchField>
        {
            new SimpleField(
                "id",
                SearchFieldDataType.String)
            {
                IsKey = true
            },

            new SearchableField("content"),

            new SimpleField(
                "source",
                SearchFieldDataType.String)
            {
                IsFilterable = true
            },

            new SimpleField(
                "chunkId",
                SearchFieldDataType.String)
            {
                IsFilterable = true
            },

            new VectorSearchField(
                "contentVector",
                1536,
                "vector-profile"),

             new SimpleField(
                "docId",
                SearchFieldDataType.String)
            {
                IsFilterable = true
            },
        };

        var vectorSearch = new VectorSearch();

        vectorSearch.Algorithms.Add(
            new HnswAlgorithmConfiguration("hnsw"));

        vectorSearch.Profiles.Add(
            new VectorSearchProfile(
                "vector-profile",
                "hnsw"));

        var index = new SearchIndex(IndexName)
        {
            Fields = fields,
            VectorSearch = vectorSearch
        };

        await _indexClient.CreateOrUpdateIndexAsync(index);
    }

    public async Task UploadChunkAsync(
        string Id,
        string docId,
        string source,
        string chunkId,
        string content,
        float[] vector)
    {
        var document = new SearchDocument
        {
            ["Id"] = Id,
            ["docId"] = docId,
            ["source"] = source,
            ["chunkId"] = chunkId,
            ["content"] = content,
            ["contentVector"] = vector
        };

        await _searchClient.UploadDocumentsAsync(
            new[] { document });
    }

    public async Task<List<SourceReference>> SearchAsync(
        float[] vector,
        int top = 5)
    {
        var options = new SearchOptions
        {
            Size = top
        };

        options.VectorSearch = new VectorSearchOptions();

        options.VectorSearch.Queries.Add(
            new VectorizedQuery(vector)
            {
                KNearestNeighborsCount = top,
                Fields = { "contentVector" }
            });

        options.Select.Add("id");
        options.Select.Add("docId");
        options.Select.Add("source");
        options.Select.Add("chunkId");
        options.Select.Add("content");
        options.Select.Add("contentVector");

        //Measure retrieval performance in C#
        var stopwatch = Stopwatch.StartNew();

        var response = await _searchClient.SearchAsync<SearchDocument>(
            null,
            options);

        var results = new List<SourceReference>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;

            var model = new SourceReference
            {
                DocumentId =
                    doc["docId"]?.ToString()
                    ?? "Unknown ID",

                Id =
                    doc["id"]?.ToString()
                    ?? "Unknown ID",

                ChunkId =
                    doc["chunkId"]?.ToString()
                    ?? string.Empty,

                DocumentName =
                    doc["source"]?.ToString()
                    ?? "Unknown document",


                Content =
                    doc["content"]?.ToString()
                    ?? string.Empty,


                //ContentVector = doc.TryGetValue("contentVector", out var vectorval)
                //    ? (vectorval as IEnumerable<object>)?
                //        .Select(x => Convert.ToSingle(x))
                //        .ToArray()
                //    : []

            };

            results.Add(model);
        }
        stopwatch.Stop();

        _logger.LogInformation(
            "RAG Retrieval Time: {Time} ms, Results: {Count}",
            stopwatch.ElapsedMilliseconds,
            results.Count);
        return results;
    }


    public async Task<List<SourceReference>> GetAllDocumentsAsync(string docid, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions
        {
            Filter = !string.IsNullOrEmpty(docid) ? $"docId eq '{docid.Replace("'", "''")}'" : "",
            Size = 1000,
            IncludeTotalCount = true
        };

        options.Select.Add("id");
        options.Select.Add("docId");
        options.Select.Add("source");
        options.Select.Add("chunkId");
        options.Select.Add("content");
        options.Select.Add("contentVector");

        var documents = new List<SourceReference>();

        var response = await _searchClient.SearchAsync<SearchDocument>(
            searchText: "*",
            options: options);

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;

            var model = new SourceReference
            {
                DocumentId =
                    doc["docId"]?.ToString()
                    ?? "Unknown ID",

                Id =
                    doc["id"]?.ToString()
                    ?? "Unknown ID",

                ChunkId =
                    doc["chunkId"]?.ToString()
                    ?? string.Empty,

                DocumentName =
                    doc["source"]?.ToString()
                    ?? "Unknown document",


                Content =
                    doc["content"]?.ToString()
                    ?? string.Empty,


                //ContentVector = doc.TryGetValue("contentVector", out var vector)
                //    ? (vector as IEnumerable<object>)?
                //        .Select(x => Convert.ToSingle(x))
                //        .ToArray()
                //    : []

            };

            documents.Add(model);

        }
        Console.WriteLine($"Total indexed chunks: {response.Value.TotalCount}");
        Console.WriteLine($"Retrieved chunks: {documents.Count}");
        return documents;
    }
    public async Task DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var results = await GetAllDocumentsAsync(documentId, cancellationToken);

        var sources = results.Select(document => document.Id?.ToString());

        // Delete in batches of up to 1000
        foreach (var batchIds in sources.Chunk(1000))
        {
            var batch = IndexDocumentsBatch.Delete(
                "id",
                batchIds);

            await _searchClient.IndexDocumentsAsync(
                batch,
                cancellationToken: cancellationToken);
        }
    }
}