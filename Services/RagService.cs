using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureRagAPI.Models;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Policy;
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
        int inputTokens = 0;
        int outputTokens = 0;
        int totalTokens = 0;

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

        //4.Construct grounded prompt.
        //string prompt1 = $"""
        //You are a document question-answering and AI assistant.

        //If relevant document context is provided:
        //- Answer using the supplied context.
        //- Do not invent facts.
        //- Cite the source document.

        //If no relevant context is provided:
        //- Answer using general knowledge.
        //- Clearly state that the answer is not based on uploaded documents.

        //If information is insufficient:
        //- Say that sufficient information is unavailable.
        //CONTEXT:
        //{context}

        //USER QUESTION:
        //{question}
        //""";

        string prompt = $"""
            You are a concise, document-grounded RAG assistant.
            Rules:
            1.Answer directly using relevant retrieved context.
            2.Never invent facts, numbers or citations.
            3.Combine relevant chunks; ignore irrelevant or duplicate content.
            4.Calculate totals only when all required values are available.
            5.Identify missing or conflicting information.
            6.Cite document name and PDF page number when available.
            7.Never assume missing retrieved information is absent from the full document.
            8.If no relevant context exists, use general knowledge only when permitted and disclose that documents were not used.
            9.If evidence is insufficient, say so.

            Return only a concise final answer. 
            CONTEXT:
            {context}

            USER QUESTION:
            {question}
            """;


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
            inputTokens = usage.GetProperty("prompt_tokens").GetInt32();

            outputTokens = usage.GetProperty("completion_tokens").GetInt32();

            totalTokens = usage.GetProperty("total_tokens").GetInt32();

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
            Sources = sources,
            InputToken = inputTokens,
            OutputToken = outputTokens,
            TotalToken = totalTokens
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