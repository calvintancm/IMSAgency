using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // Tracks supporting documents for a claim — both the CHECKLIST
    // (what documents are required, IsReceived = false until uploaded)
    // and the actual uploaded file metadata once received.
    public class ClaimDocument
    {
        [Key]
        public int DocumentId { get; set; }

        [ForeignKey(nameof(Claim))]
        public int ClaimId { get; set; }

        // Friendly name shown in the UI — "Police Report", "Workshop Estimate"
        [Required]
        [StringLength(200)]
        public string DocumentName { get; set; } = string.Empty;

        // "Police", "Workshop", "Photo", "Correspondence"
        [Required]
        [StringLength(50)]
        public string DocumentCategory { get; set; } = string.Empty;

        // false = still pending from the client/workshop;
        // true = file has been uploaded
        public bool IsReceived { get; set; } = false;

        public DateTime? ReceivedDate { get; set; }

        // Path on the server's file storage (e.g. wwwroot/uploads/claims/...)
        public string? StoragePath { get; set; }

        // The name of the file as the user uploaded it (for display/download)
        public string? OriginalFileName { get; set; }

        [StringLength(100)]
        public string? UploadedBy { get; set; }

        public DateTime? UploadedAt { get; set; }

        // ---- Navigation ----
        public Claim? Claim { get; set; }
    }
}