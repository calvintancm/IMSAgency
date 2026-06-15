using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // The motor vehicle asset register. A vehicle can be linked to MULTIPLE
    // policies over its lifetime (one per year, as it gets renewed).
    public class Vehicle
    {
        [Key]
        public int VehicleId { get; set; }

        [Required]
        [StringLength(20)]
        public string RegistrationNumber { get; set; } = string.Empty; // "WXY 1234"

        [Required]
        [StringLength(150)]
        public string MakeAndModel { get; set; } = string.Empty; // "Toyota Vios 1.5E"

        [Required]
        public int ManufactureYear { get; set; }

        [StringLength(100)]
        public string? EngineNumber { get; set; }

        [StringLength(100)]
        public string? ChassisNumber { get; set; }

        [Required]
        public int EngineCapacityCC { get; set; }

        public int SeatingCapacity { get; set; } = 5;

        // "Private", "EHailing", "CommercialGoods", "CommercialPassenger"
        [Required]
        [StringLength(30)]
        public string VehicleUsage { get; set; } = "Private";

        // Drives Feature 3 (EV Detection Banner) — when true, the policy
        // form shows the EV banner and unlocks EV-specific addon checkboxes
        public bool IsElectricVehicle { get; set; } = false;

        [StringLength(50)]
        public string? VehicleColour { get; set; }

        // Agreed value / market value used as the default Sum Insured
        // when creating a new policy for this vehicle
        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarketValue { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ---- Navigation ----
        public ICollection<Policy> Policies { get; set; } = new List<Policy>();
    }
}