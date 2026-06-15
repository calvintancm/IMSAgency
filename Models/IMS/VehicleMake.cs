using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.IMS
{
    // Master list of vehicle brands (Toyota, Honda, Perodua, Proton, etc.)
    // Used to populate the "Make" dropdown when adding a vehicle, and to
    // keep MakeAndModel entry on Vehicle consistent rather than free-typed.
    public class VehicleMake
    {
        [Key]
        public int VehicleMakeId { get; set; }

        [Required]
        [StringLength(10)]
        public string MakeCode { get; set; } = string.Empty; // "TOY", "HON", "PRD"

        [Required]
        [StringLength(80)]
        public string MakeName { get; set; } = string.Empty; // "Toyota"

        [StringLength(50)]
        public string? CountryOfOrigin { get; set; } // "Japan", "Malaysia"

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}