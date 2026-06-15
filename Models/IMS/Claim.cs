using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // The claims register. A policy can have multiple claims over its life
    // (e.g. accident claim this year, windscreen claim last year).
    public class Claim
    {
        [Key]
        public int ClaimId { get; set; }

        [ForeignKey(nameof(Policy))]
        public int PolicyId { get; set; }

        // The AGENCY's own claim reference, e.g. "CLM-2025-00045"
        [Required]
        [StringLength(50)]
        public string ClaimReferenceNumber { get; set; } = string.Empty;

        // The INSURER's claim number — filled in once the insurer registers it
        [StringLength(50)]
        public string? InsurerClaimNumber { get; set; }

        [Required]
        public DateTime ClaimDate { get; set; }

        [Required]
        public DateTime ReportedDate { get; set; }

        // "OwnDamage", "ThirdParty", "Theft", "Fire", "Natural"
        [Required]
        [StringLength(50)]
        public string ClaimType { get; set; } = string.Empty;

        // "Lodged", "InProgress", "Approved", "Rejected", "Closed"
        [Required]
        [StringLength(30)]
        public string ClaimStatus { get; set; } = "Lodged";

        [Column(TypeName = "decimal(18,2)")]
        public decimal? EstimatedLossAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ApprovedClaimAmount { get; set; }

        // Description of what happened — free text from the client/agent
        public string? ClaimNarrative { get; set; }

        [StringLength(150)]
        public string? WorkshopName { get; set; }

        // Set when ClaimStatus moves to "Closed"
        public DateTime? CloseDate { get; set; }

        public string? Remarks { get; set; }

        [StringLength(256)]
        public string? CreatedBy { get; set; }

        // Username of the agent/staff who last updated this claim
        // (e.g. changed ClaimStatus from "InProgress" to "Approved")
        [StringLength(256)]
        public string? UpdatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ---- Navigation ----
        public Policy? Policy { get; set; }
        public ICollection<ClaimDocument> ClaimDocuments { get; set; } = new List<ClaimDocument>();
    }
}