using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.ViewModels
{
    public class NonMotorPolicyFormViewModel
    {
        public int PolicyId { get; set; }
        public string CoverNoteNumber { get; set; } = "(auto-generated on save)";

        [StringLength(50)]
        public string? PolicyNumber { get; set; }

        [Required(ErrorMessage = "Please select a client")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a client")]
        public int ClientId { get; set; }
        public string? ClientDisplay { get; set; }

        [Required(ErrorMessage = "Please select an insurer")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select an insurer")]
        public int InsurerId { get; set; }

        [Required]
        public string PolicyClassCode { get; set; } = "FWHS";

        [Required]
        public string PolicyStatus { get; set; } = "Draft";

        [Required]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;

        [Required]
        [DataType(DataType.Date)]
        public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.Date.AddYears(1).AddDays(-1);

        [Range(0, 100000000)]
        public decimal SumInsured { get; set; }

        public string? AgentCode { get; set; }
        public string? Remarks { get; set; }

        // ---- Premium fields (simpler than Motor — no NCD for non-motor) ----
        [Range(0, 1000000)]
        public decimal GrossPremium { get; set; }

        [Range(0, 100000)]
        public decimal AddonSpecialPerils { get; set; }

        [Range(0, 100000)]
        public decimal AddonNamedDriver { get; set; }

        [Range(0, 100000)]
        public decimal AddonTotalLoss { get; set; }

        [Range(0, 100000)]
        public decimal? AgentCommission { get; set; }

        // ---- Computed (read-only display) ----
        public decimal TotalAddonAmount { get; set; }
        public decimal ServiceTaxAmount { get; set; }
        public decimal StampDutyAmount { get; set; } = 10.00m;
        public decimal NetPremiumPayable { get; set; }

        // ---- Flags ----
        public bool IsGroupPolicy => PolicyClassCode is "FWHS" or "PA" or "MEDHLT";
    }

    // ---- Non-Motor List ViewModels ----
    public class NonMotorPolicyListViewModel
    {
        public int TotalNonMotorPolicies { get; set; }
        public int ActiveNonMotorPolicies { get; set; }
        public int GroupPolicies { get; set; }
        public decimal TotalSumInsured { get; set; }
    }
}