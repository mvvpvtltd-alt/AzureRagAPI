namespace AzureRagAPI.Models
{
    public class PdfPageContent
    {
        public int PageNumber { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
