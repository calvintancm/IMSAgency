using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // 1:1 with Policy — PolicyId is BOTH the primary key AND foreign key
    // (configured in ApplicationDbContext.OnModelCreating), same pattern
    // as PremiumLedger. A row only exists here once a policy has actually
    // been submitted to LHDN's MyInvois e-Invoice system.
    public class LhdnEInvoiceRecord
    {
        [Key]
       // [ForeignKey(nameof(Policy))]
        public int PolicyId { get; set; }

        // The official UUID/hash returned by LHDN upon successful submission
        [Required]
        [StringLength(150)]
        public string LhdnUniqueId { get; set; } = string.Empty;

        // The agency's own internal invoice sequence number
        [Required]
        [StringLength(100)]
        public string InternalInvoiceNumber { get; set; } = string.Empty;

        [Required]
        public DateTime TransmittedAt { get; set; } = DateTime.UtcNow;

        // "Valid", "Rejected", "Pending" — drives the e-Invoice status badge (Feature 7)
        [Required]
        [StringLength(50)]
        public string ValidationStatus { get; set; } = "Pending";

        // Error/rejection message returned by LHDN's IRBM API, if any
        public string? IrbmErrorMessage { get; set; }

        // ---- Navigation ----
        public Policy? Policy { get; set; }
    }
}