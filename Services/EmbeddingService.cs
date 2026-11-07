using System.Net.Http.Json;
using System.Text.Json;

namespace AzureRagAPI.Services;

public sealed class EmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public EmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        string deployment =
            _configuration["AzureAI:EmbeddingDeployment"]
            ?? throw new InvalidOperationException(
                "Embedding deployment is missing.");

        var request = new
        {
            model = deployment,
            input = text
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "embeddings",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var embedding = document.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");

        return embedding
            .EnumerateArray()
            .Select(x => x.GetSingle())
            .ToArray();
    }
}