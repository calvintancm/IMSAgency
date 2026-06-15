using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // Malaysian No-Claim Discount tariff table.
    // Used to populate the NCD % dropdown on the policy form and to
    // validate/cross-check the NcdPercentage entered against ClaimFreeYears.
    public class NcdRate
    {
        [Key]
        public int NcdRateId { get; set; }

        // Number of consecutive claim-free years (1-5; 5 = "5 or more")
        [Required]
        public int ClaimFreeYears { get; set; }

        // The corresponding discount percentage: 25, 30, 38.33, 45, 55
        [Required]
        [Column(TypeName = "decimal(5,2)")]
        public decimal NcdPercentage { get; set; }

        // Tax/tariff year this rate applies to — Malaysian tariffs can be
        // revised, so we keep this versioned by year rather than overwriting
        [Required]
        public int EffectiveYear { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}