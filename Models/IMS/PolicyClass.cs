using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.IMS
{
    // Master list of insurance product types (Motor Private Car, FWHS, PA, etc.)
    // Policy.PolicyClassCode is a foreign key into this table's ClassCode column
    // (a unique alternate key, configured in ApplicationDbContext).
    public class PolicyClass
    {
        [Key]
        public int PolicyClassId { get; set; }

        [Required]
        [StringLength(20)]
        public string ClassCode { get; set; } = string.Empty; // "MTRCAR", "FWHS", "PA"

        [Required]
        [StringLength(100)]
        public string ClassName { get; set; } = string.Empty; // "Motor Private Car"

        // "Motor" or "NonMotor" — used to split the sidebar into
        // "Motor Policies" vs "Non-Motor Policies" sections
        [Required]
        [StringLength(20)]
        public string ClassCategory { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        // Controls the order this class appears in dropdowns (lower = first)
        public int DisplayOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ---- Navigation ----
        public ICollection<Policy> Policies { get; set; } = new List<Policy>();
    }
}