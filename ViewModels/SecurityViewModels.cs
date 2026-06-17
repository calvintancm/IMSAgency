using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.ViewModels
{
    // ---- User Management ----
    public class UserListItem
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsLockedOut { get; set; }
        public string Roles { get; set; } = string.Empty;
        public string? LinkedAgentCode { get; set; }
    }

    public class CreateUserViewModel
    {
        [Required]
        [StringLength(256)]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "Agent";

        public string? AgentCode { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class ResetPasswordViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;
    }

    // ---- LHDN e-Invoice ----
    public class LhdnEInvoiceListViewModel
    {
        public int TotalInvoices { get; set; }
        public int ValidCount { get; set; }
        public int PendingCount { get; set; }
        public int RejectedCount { get; set; }
    }
}