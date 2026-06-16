using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.ViewModels
{
    public class VehicleFormViewModel
    {
        public int VehicleId { get; set; }

        [Required(ErrorMessage = "Registration Number is required")]
        [StringLength(20)]
        public string RegistrationNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Make & Model is required")]
        [StringLength(150)]
        public string MakeAndModel { get; set; } = string.Empty;

        [Required]
        [Range(1950, 2100, ErrorMessage = "Enter a valid manufacture year")]
        public int ManufactureYear { get; set; } = DateTime.UtcNow.Year;

        [StringLength(100)]
        public string? EngineNumber { get; set; }

        [StringLength(100)]
        public string? ChassisNumber { get; set; }

        [Required]
        [Range(0, 10000, ErrorMessage = "Enter engine capacity in CC (0 for electric vehicles)")]
        public int EngineCapacityCC { get; set; }

        [Range(1, 100)]
        public int SeatingCapacity { get; set; } = 5;

        [Required]
        public string VehicleUsage { get; set; } = "Private";

        public bool IsElectricVehicle { get; set; } = false;

        [StringLength(50)]
        public string? VehicleColour { get; set; }

        [Range(0, 100000000)]
        public decimal? MarketValue { get; set; }

        public bool IsActive { get; set; } = true;
    }
}