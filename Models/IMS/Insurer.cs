using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // Master list of insurance companies the agency works with
    // (MSIG, Allianz, Etiqa, etc.)
    public class Insurer
    {
        [Key]
        public int InsurerId { get; set; }

        [Required]
        [StringLength(10)]
        public string InsurerCode { get; set; } = string.Empty; // "MSIG", "ALZ"

        [Required]
        [StringLength(150)]
        public string InsurerName { get; set; } = string.Empty; // "MSIG Insurance (Malaysia) Bhd"

        // "Motor", "NonMotor", or "Both" — controls which dropdowns this insurer shows up in
        [Required]
        [StringLength(20)]
        public string InsurerType { get; set; } = string.Empty;

        [StringLength(30)]
        public string? ContactPhone { get; set; }

        [StringLength(100)]
        public string? ContactEmail { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ---- Navigation ----
        //public ICollection<Policy> Policies { get; set; } = new List<Policy>();
    }
}