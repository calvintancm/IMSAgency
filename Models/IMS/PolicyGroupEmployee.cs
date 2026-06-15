using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // Used for GROUP policies only — FWHS (Foreign Worker Hospitalisation),
    // PA Group, Medical Group. Each row is one covered worker/employee.
    // Shown as a collapsible sub-grid under the policy (Feature 5),
    // with an Excel export of the full roster.
    public class PolicyGroupEmployee
    {
        [Key]
        public int EmployeeLinkId { get; set; }

        [ForeignKey(nameof(Policy))]
        public int PolicyId { get; set; }

        [Required]
        [StringLength(255)]
        public string EmployeeName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string PassportOrNric { get; set; } = string.Empty;

        // "M" or "F"
        [StringLength(10)]
        public string Gender { get; set; } = "M";

        // ISO Alpha-3 country code — "BGD" (Bangladesh), "NPL" (Nepal),
        // "MYS" (Malaysia), "IDN" (Indonesia), etc.
        [StringLength(3)]
        public string? NationalityCode { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [StringLength(150)]
        public string? Occupation { get; set; }

        // Used for SOCSO / workmen's compensation calculations on
        // FWHS-type policies
        [Column(TypeName = "decimal(18,2)")]
        public decimal? AnnualWage { get; set; }

        public bool IsActive { get; set; } = true;

        // ---- Navigation ----
        public Policy? Policy { get; set; }
    }
}