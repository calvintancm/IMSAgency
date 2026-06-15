using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // A client can have multiple addresses (residential, postal, business).
    // State will be populated from the "Malaysian States" master dropdown
    // (MasterController/MalaysianStates) — that one is hardcoded in a
    // static list rather than a database table, same approach as MVA's
    // static lookup lists.
    public class ClientAddress
    {
        [Key]
        public int AddressId { get; set; }

        [ForeignKey(nameof(Client))]
        public int ClientId { get; set; }

        // "Residential", "Postal", "Business"
        [Required]
        [StringLength(20)]
        public string AddressType { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string AddressLine1 { get; set; } = string.Empty;

        [StringLength(200)]
        public string? AddressLine2 { get; set; }

        [Required]
        [StringLength(80)]
        public string City { get; set; } = string.Empty;

        // Malaysian state, e.g. "Selangor", "Johor", "Pulau Pinang"
        [Required]
        [StringLength(50)]
        public string State { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        public string Postcode { get; set; } = string.Empty;

        // Only ONE address per client should have this set to true —
        // enforced in the controller (same rule as ClientPhone.IsPrimary)
        public bool IsPrimary { get; set; } = false;

        // ---- Navigation ----
        public Client? Client { get; set; }
    }
}