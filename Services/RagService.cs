using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureRagAPI.Models;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace AzureRagAPI.Services;

public sealed class RagService
{
    private readonly EmbeddingService _embeddingService;
    private readonly SearchService _searchService;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    public RagService(
        EmbeddingService embeddingService,
        SearchService searchService,
        HttpClient httpClient,
        IConfiguration configuration, 
        ILogger<RagService> logger)
    {
        _embeddingService = embeddingService;
        _searchService = searchService;
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AskResponse> AskAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        await _searchService.CreateIndexIfNotExistsAsync();

        //Measure the complete RAG pipeline
        // Main Timer started
        var totalTimer = Stopwatch.StartNew();

        // Embedding Timer started
        var embeddingTimer = Stopwatch.StartNew();

        // 1. Generate the question embedding.
        float[] questionVector =
            await _embeddingService.GenerateEmbeddingAsync(
                question,
                cancellationToken);

        // Embedding Timer stopped
        embeddingTimer.Stop();

        // Search Timer started
        var searchTimer = Stopwatch.StartNew();

        // 2. Retrieve relevant document chunks.
        List<SourceReference> documents =
            await _searchService.SearchAsync(
                questionVector,
                top: 3);

        // Search Timer stopped
        searchTimer.Stop();
        string context = string.Empty;
        if (documents.Count != 0)
        {
            //return new AskResponse
            //{
            //    Answer = "No documents uploaded."
            //};
  
            // 3. Build context.
            context = string.Join(
                "\n\n",
                documents.Select(document =>
                    $"Source: {document.DocumentName}\n" +
                    $"Content: {document.Content}"));
        }

        // 4. Construct grounded prompt.
        string prompt = $"""
        You are a document question-answering and AI assistant.

        If relevant document context is provided:
        - Answer using the supplied context.
        - Do not invent facts.
        - Cite the source document.

        If no relevant context is provided:
        - Answer using general knowledge.
        - Clearly state that the answer is not based on uploaded documents.

        If information is insufficient:
        - Say that sufficient information is unavailable.

        CONTEXT:
        {context}

        USER QUESTION:
        {question}
        """;

        //string prompt = $"""
        //You are an AI assistant powered by a Retrieval-Augmented Generation (RAG) system.

        //Follow these rules:

        //1. When relevant document context is provided:
        //   - Answer using only the relevant supplied document content.
        //   - Ignore unrelated documents and irrelevant context.
        //   - Combine information from multiple relevant chunks when necessary.
        //   - Do not invent missing policy details.
        //   - Provide a clear, structured and comprehensive answer.
        //   - Cite the source document and section wherever available.

        //2. When no relevant document context is available:
        //   - Answer using general knowledge.
        //   - Clearly state that the answer is not based on uploaded documents.

        //3. When relevant context exists but does not contain sufficient information:
        //   - Clearly identify what information is unavailable.
        //   - Do not fill gaps with assumptions.

        //4. Keep answers concise but sufficiently detailed to address the user's question.

        //5. Never claim that a document contains information that is not present in the supplied context.

        //CONTEXT:
        //{context}

        //USER QUESTION:
        //{question}
        //""";
        _logger.LogInformation(prompt);

        // 5. Call GPT-5.5.
        string deployment =
            _configuration["AzureAI:ChatDeployment"]
            ?? throw new InvalidOperationException(
                "Chat deployment is missing.");

        var request = new
        {
            model = deployment,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            //max_tokens = 1500
            //max_completion_tokens = 1500
        };
        // GPT Timer stopped
        var GPTTimer = Stopwatch.StartNew();

        using var response = await _httpClient.PostAsJsonAsync(
            "chat/completions",
            request,
            cancellationToken);

        // Context Timer stopped
        GPTTimer.Stop();


        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(
                cancellationToken),
            cancellationToken: cancellationToken);

        string answer = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        _logger.LogInformation(
    "GPT Answer Length: {Length}",
    answer.Length);


        if (json.RootElement.TryGetProperty("usage", out var usage))
        {
            int inputTokens = usage.GetProperty("prompt_tokens").GetInt32();

            int outputTokens = usage.GetProperty("completion_tokens").GetInt32();

            int totalTokens = usage.GetProperty("total_tokens").GetInt32();

            _logger.LogInformation(
                "GPT Token Usage: Input={InputTokens}, Output={OutputTokens}, Total={TotalTokens}",
                inputTokens,
                outputTokens,
                totalTokens);
        }
        else
        {
            _logger.LogWarning("GPT usage information not available in response.");
        }

        // 6. Return answer and sources.
        var sources = documents
            .DistinctBy(source => source.ChunkId)
            .ToList();

        totalTimer.Stop();

        _logger.LogInformation(
            "RAG Metrics: Total={Total}ms, Embedding={Embedding}ms, " +
            "Search={Search}ms, GPT={GPT}ms",
            totalTimer.ElapsedMilliseconds,
            embeddingTimer.ElapsedMilliseconds,
            searchTimer.ElapsedMilliseconds,
            GPTTimer.ElapsedMilliseconds);


        return new AskResponse
        {
            Answer = answer,
            Sources = sources
        };
    }


    public async Task<AskResponse> GetAllDocumentsAsync(string docid,
        CancellationToken cancellationToken = default)
    {
        // 1. Retrieve relevant document chunks.
        List<SourceReference> documents =
            await _searchService.GetAllDocumentsAsync(docid, cancellationToken);

        if (documents.Count == 0)
        {
            return new AskResponse
            {
                Answer = "No documents uploaded."
            };
        }

        // 2. Return answer and sources.
        var sources = documents.DistinctBy(source => source.DocumentId).Select(doc => new SourceReference { DocumentId = doc.DocumentId, DocumentName = doc.DocumentName }).ToList();

        return new AskResponse
        {
            Sources = sources
        };
    }

    public async Task DeleteDocumentAsync(string documentId,
        CancellationToken cancellationToken = default)
    {
        await _searchService.DeleteDocumentAsync(
        documentId,
        cancellationToken);
    }
}