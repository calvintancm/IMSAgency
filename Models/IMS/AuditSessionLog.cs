using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.IMS
{
    // Login/logout security log — written by AuthController on every
    // successful login and logout (see WriteAuditLogAsync). Shown on
    // SecurityController/AuditLog (Step 9.1).
    public class AuditSessionLog
    {
        [Key]
        public int SessionId { get; set; }

        // FK -> AspNetUsers.Id (string)
        [Required]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [StringLength(256)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(45)] // long enough for IPv6
        public string IpAddress { get; set; } = string.Empty;

        public string? UserAgent { get; set; }

        [Required]
        public DateTime LoginTime { get; set; } = DateTime.UtcNow;

        public DateTime? LogoutTime { get; set; }

        // true = session still open (user hasn't logged out yet)
        public bool IsActive { get; set; } = true;

        // "UserLogout", "SessionExpired", "ForcedLogout", etc.
        [StringLength(50)]
        public string? LogoutReason { get; set; }
    }
}