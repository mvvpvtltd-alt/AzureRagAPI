using AzureRagAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzureRagAPI.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;

    public DocumentsController(
        DocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(
                "Please upload a valid document.");
        }

        int chunks = await _documentService.ProcessDocumentAsync(
            file,
            cancellationToken);

        return Ok(new
        {
            message = "Document processed successfully.",
            document = file.FileName,
            chunks
        });
    }
}