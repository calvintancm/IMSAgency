using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.ViewModels
{
    public class MotorPolicyFormViewModel
    {
        public int PolicyId { get; set; }
        public string CoverNoteNumber { get; set; } = "(auto-generated on save)";

        [StringLength(50)]
        public string? PolicyNumber { get; set; }

        [Required(ErrorMessage = "Please select a client")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a client")]
        public int ClientId { get; set; }
        public string? ClientDisplay { get; set; }

        [Required(ErrorMessage = "Please select a vehicle")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a vehicle")]
        public int VehicleId { get; set; }
        public string? VehicleDisplay { get; set; }

        [Required(ErrorMessage = "Please select an insurer")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select an insurer")]
        public int InsurerId { get; set; }

        [Required]
        public string PolicyClassCode { get; set; } = "MTRCAR";

        [Required]
        public string PolicyStatus { get; set; } = "Draft";

        [Required]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;

        [Required]
        [DataType(DataType.Date)]
        public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.Date.AddYears(1).AddDays(-1);

        [Range(0, 100, ErrorMessage = "NCD % must be between 0 and 100")]
        public decimal NcdPercentage { get; set; } = 0;

        [Range(0, 100000000, ErrorMessage = "Sum Insured must be a positive value")]
        public decimal SumInsured { get; set; }

        public string? AgentCode { get; set; }

        public string? Remarks { get; set; }

        // ---- Premium Calculator fields (Feature 2) ----
        [Range(0, 1000000, ErrorMessage = "Gross Premium must be a positive value")]
        public decimal GrossPremium { get; set; }

        [Range(0, 100000)]
        public decimal AddonWindscreen { get; set; }

        [Range(0, 100000)]
        public decimal AddonSpecialPerils { get; set; }

        [Range(0, 100000)]
        public decimal AddonNamedDriver { get; set; }

        [Range(0, 100000)]
        public decimal AddonTotalLoss { get; set; }

        // EV Charger Extension — only relevant when the linked vehicle IsElectricVehicle = true
        [Range(0, 100000)]
        public decimal AddonEvCharger { get; set; }

        [Range(0, 100000)]
        public decimal? AgentCommission { get; set; }

        // Read-only flags for the view (not posted back)
        public bool IsElectricVehicle { get; set; }

        // Computed totals — read-only, recalculated server-side regardless
        // of what the browser sends (server is the source of truth)
        public decimal NcdDiscountAmount { get; set; }
        public decimal NetPremium { get; set; }
        public decimal TotalAddonAmount { get; set; }
        public decimal ServiceTaxAmount { get; set; }
        public decimal StampDutyAmount { get; set; } = 10.00m;
        public decimal NetPremiumPayable { get; set; }
    }
}