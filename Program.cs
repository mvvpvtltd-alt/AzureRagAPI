using Azure.Search.Documents;
using AzureRagAPI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Azure AI Search.
builder.Services.AddSingleton<SearchService>();

// Embedding service.
builder.Services.AddHttpClient<EmbeddingService>((serviceProvider, client) =>
{
    var configuration =
        serviceProvider.GetRequiredService<IConfiguration>();

    string endpoint =
        configuration["AzureAI:OpenAIEndpoint"]
        ?? throw new InvalidOperationException(
            "AzureAI:OpenAIEndpoint is missing.");

    string apiKey =
        configuration["AzureAI:ApiKey"]
        ?? throw new InvalidOperationException(
            "AzureAI:ApiKey is missing.");

    string baseUrl = endpoint.TrimEnd('/') + "/";

    client.BaseAddress = new Uri(baseUrl);

    client.DefaultRequestHeaders.Add(
        "api-key",
        apiKey);
});

// Document processing.
builder.Services.AddScoped<DocumentService>();

// RAG service.
builder.Services.AddHttpClient<RagService>((serviceProvider, client) =>
{
    var configuration =
        serviceProvider.GetRequiredService<IConfiguration>();

    string endpoint =
        configuration["AzureAI:OpenAIEndpoint"]
        ?? throw new InvalidOperationException(
            "AzureAI:OpenAIEndpoint is missing.");

    string apiKey =
        configuration["AzureAI:ApiKey"]
        ?? throw new InvalidOperationException(
            "AzureAI:ApiKey is missing.");

    client.BaseAddress = new Uri(
        endpoint.TrimEnd('/') + "/");

    client.DefaultRequestHeaders.Add(
        "api-key",
        apiKey);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();