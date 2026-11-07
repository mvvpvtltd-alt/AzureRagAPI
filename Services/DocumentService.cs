using AzureRagAPI.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using UglyToad.PdfPig;

namespace AzureRagAPI.Services;

public sealed class DocumentService
{
    private readonly EmbeddingService _embeddingService;
    private readonly SearchService _searchService;

    public DocumentService(
        EmbeddingService embeddingService,
        SearchService searchService)
    {
        _embeddingService = embeddingService;
        _searchService = searchService;
    }

    public async Task<int> ProcessDocumentAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        string extension =
            Path.GetExtension(file.FileName).ToLowerInvariant();

        string text;

        await using var stream = file.OpenReadStream();

        if (extension == ".pdf")
        {
            text = ExtractPdfText(stream);
        }
        else if (extension == ".docx")
        {
            text = ExtractDocxText(stream);
        }
        else
        {
            throw new InvalidOperationException(
                "Only PDF and DOCX files are supported.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                "No readable text was found in the document.");
        }

        await _searchService.CreateIndexIfNotExistsAsync();

        var chunks = SplitText(text);

        int count = 0;

        string docId = Guid.NewGuid().ToString("N");

        foreach (string chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string chunkId = Guid.NewGuid().ToString("N");

            float[] vector =
                await _embeddingService.GenerateEmbeddingAsync(
                    chunk,
                    cancellationToken);

            await _searchService.UploadChunkAsync(
                Id: chunkId,
                docId: docId,
                source: file.FileName,
                chunkId: chunkId,
                content: chunk,
                vector: vector);

            count++;
        }

        return count;
    }

    private static string ExtractPdfText(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);

        return string.Join(
             Environment.NewLine,
             pdf.GetPages().Select(page => page.Text));
    }

    private static string ExtractDocxText(Stream stream)
    {
        using var document = WordprocessingDocument.Open(
            stream,
            false);

        return document.MainDocumentPart?
            .Document
            .Body?
            .InnerText ?? string.Empty;
    }

    public static List<string> SplitText(
    string text,
    int chunkSize = 2000)
    {
        var paragraphs = text.Split(
            new[] { "\r\n", "\n" },
            StringSplitOptions.RemoveEmptyEntries);

        var chunks = new List<string>();
        var currentChunk = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            string cleaned = paragraph.Trim();

            if (string.IsNullOrWhiteSpace(cleaned))
                continue;

            if (currentChunk.Length + cleaned.Length > chunkSize
                && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }

            if (currentChunk.Length > 0)
                currentChunk.AppendLine();

            currentChunk.Append(cleaned);
        }

        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString().Trim());

        return chunks;
    }
    //private static List<string> SplitText(
    //    string text,
    //    int chunkSize = 500,
    //    int overlap = 100)
    //{
    //    var chunks = new List<string>();

    //    text = text
    //        .Replace("\r", " ")
    //        .Replace("\n", " ")
    //        .Trim();

    //    int start = 0;

    //    while (start < text.Length)
    //    {
    //        int length = Math.Min(
    //            chunkSize,
    //            text.Length - start);

    //        string chunk = text
    //            .Substring(start, length)
    //            .Trim();

    //        if (!string.IsNullOrWhiteSpace(chunk))
    //        {
    //            chunks.Add(chunk);
    //        }

    //        if (start + length >= text.Length)
    //            break;

    //        start += chunkSize - overlap;
    //    }

    //    return chunks;
    //}
}