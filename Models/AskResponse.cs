namespace AzureRagAPI.Models;

public sealed class AskResponse
{
    public string Answer { get; set; } = string.Empty;

    public List<SourceReference> Sources { get; set; } = [];
}

public sealed class SourceReference
{
    public string DocumentId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float[]? ContentVector { get; set; }
}