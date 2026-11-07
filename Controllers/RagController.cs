using AzureRagAPI.Models;
using AzureRagAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzureRagAPI.Controllers;

[ApiController]
[Route("api/rag")]
public sealed class RagController : ControllerBase
{
    private readonly RagService _ragService;

    public RagController(RagService ragService)
    {
        _ragService = ragService;
    }

    [HttpGet("alldata")]
    public async Task<IActionResult> GetAllDocuments([FromQuery] string? docid)
    {
        var documents =
            await _ragService.GetAllDocumentsAsync(docid!);

        return Ok(documents);
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AskResponse>> Ask(
        [FromBody] AskRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _ragService.AskAsync(
            request.Question,
            cancellationToken);

        return Ok(response);
    }

    [HttpDelete("{documentId}")]
    public async Task<IActionResult> DeleteDocument(
    string documentId,
    CancellationToken cancellationToken)
    {
        await _ragService.DeleteDocumentAsync(
            documentId,
            cancellationToken);

        return Ok(new
        {
            Message = "Document chunks deleted successfully",
            DocumentId = documentId
        });
    }
}