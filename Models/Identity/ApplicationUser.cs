// Models/Identity/ApplicationUser.cs
using Microsoft.AspNetCore.Identity;

namespace ImsAgency.Web.Models.Identity
{
    // This inherits from IdentityUser, so it AUTOMATICALLY gets:
    // Id, UserName, Email, PasswordHash, PhoneNumber, etc.
    // We only ADD the extra columns IMS Agency needs.
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;

        // Optional link to AgentProfile (an agent record in our own tables)
        public int? AgentId { get; set; }

        public bool IsActive { get; set; } = true;
    }
}