using System.ComponentModel.DataAnnotations;

namespace AzureRagAPI.Models
{
    public sealed class AskRequest
    {
        [Required]
        [MinLength(1)]
        public string Question { get; set; } = string.Empty;
    }
}
