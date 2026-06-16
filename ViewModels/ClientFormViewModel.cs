using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.ViewModels
{
    // Used by BOTH AddClient and EditClient — same view, same model.
    // ClientId = 0 means "new client" (sub-grids for phones/addresses
    // are disabled until the client is saved and gets a real ClientId).
    public class ClientFormViewModel
    {
        public int ClientId { get; set; }

        // Shown read-only — auto-generated on first save, never editable
        public string ClientCode { get; set; } = "(auto-generated on save)";

        [Required(ErrorMessage = "Client Type is required")]
        public string ClientType { get; set; } = "Individual";

        [Required(ErrorMessage = "Client Name is required")]
        [StringLength(255)]
        public string ClientName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Identification Number is required")]
        [StringLength(100)]
        public string IdentificationNumber { get; set; } = string.Empty;

        [Required]
        public string IdentificationType { get; set; } = "NRIC";

        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        public string Gender { get; set; } = "Unknown";

        [StringLength(150)]
        [EmailAddress(ErrorMessage = "Enter a valid email address")]
        public string? EmailAddress { get; set; }

        [StringLength(100)]
        public string? OccupationOrBusiness { get; set; }

        public bool IsActive { get; set; } = true;

        public string? Remarks { get; set; }
    }
}