using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.ViewModels
{
    public class ClaimListViewModel
    {
        public int TotalClaims { get; set; }
        public int OpenClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public decimal TotalApprovedAmount { get; set; }
    }

    public class LodgeClaimViewModel
    {
        public int ClaimId { get; set; }

        [Required(ErrorMessage = "Please select a policy")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a policy")]
        public int PolicyId { get; set; }
        public string? PolicyDisplay { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime ClaimDate { get; set; } = DateTime.UtcNow.Date;

        [Required]
        [DataType(DataType.Date)]
        public DateTime ReportedDate { get; set; } = DateTime.UtcNow.Date;

        [Required(ErrorMessage = "Claim Type is required")]
        public string ClaimType { get; set; } = "OwnDamage";

        public string ClaimStatus { get; set; } = "Lodged";

        [Range(0, 100000000)]
        public decimal? EstimatedLossAmount { get; set; }

        [Range(0, 100000000)]
        public decimal? ApprovedClaimAmount { get; set; }

        public string? ClaimNarrative { get; set; }

        [StringLength(150)]
        public string? WorkshopName { get; set; }

        [DataType(DataType.Date)]
        public DateTime? CloseDate { get; set; }

        public string? Remarks { get; set; }
    }
}