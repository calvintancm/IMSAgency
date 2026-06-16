using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // The customer registry — both individuals and companies.
    // This is the "hub" that Policies, Payments, and (eventually)
    // the Client 360° View page all revolve around.
    public class Client
    {
        [Key]
        public int ClientId { get; set; }

        // Auto-generated on save: "CLT-2024-0001" (year + sequence)
        // We generate this in the controller, not the database,
        // so we can format it exactly like the spec.
        [Required]
        [StringLength(20)]
        public string ClientCode { get; set; } = string.Empty;

        // "Individual" or "Company"
        [Required]
        [StringLength(20)]
        public string ClientType { get; set; } = string.Empty;

        // For Individuals: full name. For Companies: registered company name.
        [Required]
        [StringLength(255)]
        public string ClientName { get; set; } = string.Empty;

        // NRIC number, Passport number, or Business Registration Number,
        // depending on IdentificationType below
        [Required]
        [StringLength(100)]
        public string IdentificationNumber { get; set; } = string.Empty;

        // "NRIC", "Passport", or "BRN"
        [Required]
        [StringLength(20)]
        public string IdentificationType { get; set; } = string.Empty;

        // Only meaningful for Individuals — left null for Companies
        public DateTime? DateOfBirth { get; set; }

        // "Male", "Female", "Unknown" — "Unknown" used for Companies
        [StringLength(10)]
        public string Gender { get; set; } = "Unknown";

        [StringLength(150)]
        public string? EmailAddress { get; set; }

        [StringLength(100)]
        public string? OccupationOrBusiness { get; set; }

        // Fraud/risk flag — blacklisted clients are filtered out of
        // normal client lists and shown in a separate "Blacklisted" view
        public bool IsBlacklisted { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // ---- Navigation ----
        public ICollection<ClientPhone> ClientPhones { get; set; } = new List<ClientPhone>();
        public ICollection<ClientAddress> ClientAddresses { get; set; } = new List<ClientAddress>();
        public ICollection<Policy> Policies { get; set; } = new List<Policy>();
      
        public ICollection<CustomerPaymentLedger> CustomerPaymentLedgers { get; set; } = new List<CustomerPaymentLedger>();
    }
}