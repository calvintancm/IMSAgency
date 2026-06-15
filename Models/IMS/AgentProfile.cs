using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // A staff/agent profile. Optionally linked to an AspNetUsers row via
    // IdentityUserId — every login-capable staff member should have a
    // matching AgentProfile, but AgentProfile can also exist standalone
    // (e.g. for an agent who doesn't log into the system yet).
    public class AgentProfile
    {
        [Key]
        public int AgentId { get; set; }

        // FK -> AspNetUsers.Id (string, since Identity uses string keys).
        // Nullable because not every agent necessarily has a login.
        [StringLength(450)] // matches AspNetUsers.Id max length
        public string? IdentityUserId { get; set; }

        // Internal staff code shown on policies/claims, e.g. "AGT-001"
        [Required]
        [StringLength(20)]
        public string AgentCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        // "Admin", "SeniorAgent", "Agent", "Support" — mirrors the
        // AspNetRoles names so the profile and login role stay in sync
        [Required]
        [StringLength(30)]
        public string AgentType { get; set; } = "Agent";

        // PIA/PIAM agent license number (Malaysian insurance agent licensing)
        [StringLength(50)]
        public string? LicenseNumber { get; set; }

        public DateTime? LicenseExpiryDate { get; set; }

        [Required]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string MobileNumber { get; set; } = string.Empty;

        public DateTime? JoinedDate { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Policy> Policies { get; set; } = new List<Policy>();
    }
}