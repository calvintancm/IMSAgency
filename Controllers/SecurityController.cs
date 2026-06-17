using ImsAgency.Web.Data;
using ImsAgency.Web.Models.Identity;
using ImsAgency.Web.Models.IMS;
using ImsAgency.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class SecurityController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<SecurityController> _logger;

        public SecurityController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<SecurityController> logger)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
        }

        private static readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { PropertyNamingPolicy = null };

        private JsonResult GridResult(object data, int total) =>
            Json(new { Data = data, Total = total, Errors = (object?)null }, _jsonOptions);

        private JsonResult GridError(string message) =>
            Json(new
            {
                Data = Array.Empty<object>(),
                Total = 0,
                Errors = new Dictionary<string, object>
                {
                    ["error"] = new { errors = new[] { message } }
                }
            }, _jsonOptions);


        // ════════════════════════════════════════════════════════════════
        // SECTION: USER MANAGEMENT
        // ════════════════════════════════════════════════════════════════

        // GET: /Security/Users
        public IActionResult Users() => View();

        // POST: /Security/UsersRead
        [HttpPost]
        public async Task<JsonResult> UsersRead(
            int page = 1, int pageSize = 15,
            string? searchText = null)
        {
            try
            {
                // Load users + their roles + linked agent code
                var users = await _userManager.Users
                    .OrderBy(u => u.UserName)
                    .ToListAsync();

                var result = new List<UserListItem>();

                foreach (var u in users)
                {
                    var roles = await _userManager.GetRolesAsync(u);
                    var agent = await _db.AgentProfiles
                        .Where(a => a.IdentityUserId == u.Id)
                        .Select(a => a.AgentCode)
                        .FirstOrDefaultAsync();

                    result.Add(new UserListItem
                    {
                        UserId = u.Id,
                        UserName = u.UserName ?? "",
                        FullName = u.FullName,
                        Email = u.Email ?? "",
                        IsActive = u.IsActive,
                        IsLockedOut = await _userManager.IsLockedOutAsync(u),
                        Roles = string.Join(", ", roles),
                        LinkedAgentCode = agent
                    });
                }

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim().ToLower();
                    result = result
                        .Where(u => u.UserName.ToLower().Contains(s)
                                 || u.FullName.ToLower().Contains(s)
                                 || u.Email.ToLower().Contains(s))
                        .ToList();
                }

                var total = result.Count;
                var data = result
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Users grid");
                return GridError("Failed to load users.");
            }
        }

        // POST: /Security/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> CreateUser(CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var err = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Validation failed.";
                return Json(new { success = false, message = err });
            }

            try
            {
                // Check username / email uniqueness
                if (await _userManager.FindByNameAsync(model.UserName) != null)
                    return Json(new { success = false, message = $"Username '{model.UserName}' already exists." });

                if (await _userManager.FindByEmailAsync(model.Email) != null)
                    return Json(new { success = false, message = $"Email '{model.Email}' already exists." });

                var user = new ApplicationUser
                {
                    UserName = model.UserName,
                    NormalizedUserName = model.UserName.ToUpper(),
                    Email = model.Email,
                    NormalizedEmail = model.Email.ToUpper(),
                    FullName = model.FullName,
                    EmailConfirmed = true,
                    IsActive = model.IsActive,
                    LockoutEnabled = true
                };

                var createResult = await _userManager.CreateAsync(user, model.Password);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                    return Json(new { success = false, message = errors });
                }

                // Assign role
                if (!string.IsNullOrWhiteSpace(model.Role))
                {
                    await _userManager.AddToRoleAsync(user, model.Role);
                }

                // Link to AgentProfile if AgentCode provided
                if (!string.IsNullOrWhiteSpace(model.AgentCode))
                {
                    var agent = await _db.AgentProfiles
                        .FirstOrDefaultAsync(a => a.AgentCode == model.AgentCode);

                    if (agent != null)
                    {
                        agent.IdentityUserId = user.Id;
                        await _db.SaveChangesAsync();
                    }
                }

                return Json(new { success = true, message = $"User '{model.UserName}' created successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return Json(new { success = false, message = "Failed to create user. " + ex.Message });
            }
        }

        // POST: /Security/ToggleUserActive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ToggleUserActive(string UserId)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(UserId);
                if (user == null) return GridError("User not found.");

                // Prevent deactivating yourself
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser?.Id == UserId)
                    return GridError("You cannot deactivate your own account.");

                user.IsActive = !user.IsActive;
                await _userManager.UpdateAsync(user);

                return GridResult(new[] { new { UserId, user.IsActive } }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling user active");
                return GridError("Failed to update user. " + ex.Message);
            }
        }

        // POST: /Security/UnlockUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> UnlockUser(string UserId)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(UserId);
                if (user == null) return GridError("User not found.");

                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(-1));
                await _userManager.ResetAccessFailedCountAsync(user);

                return Json(new { success = true, message = $"Account '{user.UserName}' unlocked." },
                    _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unlocking user");
                return GridError("Failed to unlock user. " + ex.Message);
            }
        }

        // POST: /Security/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ResetPassword(ResetPasswordViewModel model)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(model.UserId);
                if (user == null) return GridError("User not found.");

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                    return Json(new { success = false, message = errors });
                }

                return Json(new { success = true, message = $"Password for '{user.UserName}' reset successfully." },
                    _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password");
                return Json(new { success = false, message = "Failed to reset password. " + ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: ROLE MANAGEMENT
        // ════════════════════════════════════════════════════════════════

        // GET: /Security/Roles
        public IActionResult Roles() => View();

        // POST: /Security/RolesRead
        [HttpPost]
        public async Task<JsonResult> RolesRead()
        {
            try
            {
                var roles = await _roleManager.Roles
                    .OrderBy(r => r.Name)
                    .ToListAsync();

                var data = new List<object>();
                foreach (var r in roles)
                {
                    var userCount = (await _userManager.GetUsersInRoleAsync(r.Name!)).Count;
                    data.Add(new
                    {
                        RoleId = r.Id,
                        RoleName = r.Name,
                        UserCount = userCount
                    });
                }

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Roles");
                return GridError("Failed to load roles.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: AGENT PROFILES
        // ════════════════════════════════════════════════════════════════

        // GET: /Security/AgentProfiles
        public IActionResult AgentProfiles() => View();

        // POST: /Security/AgentProfilesRead
        [HttpPost]
        public async Task<JsonResult> AgentProfilesRead(
            int page = 1, int pageSize = 15,
            string? searchText = null,
            string? agentType = null)
        {
            try
            {
                var query = _db.AgentProfiles.AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(a =>
                        a.AgentCode.Contains(s) ||
                        a.FullName.Contains(s) ||
                        a.Email.Contains(s));
                }

                if (!string.IsNullOrWhiteSpace(agentType) && agentType != "All")
                    query = query.Where(a => a.AgentType == agentType);

                var total = await query.CountAsync();

                var data = await query
                    .OrderBy(a => a.AgentCode)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(a => new
                    {
                        a.AgentId,
                        a.AgentCode,
                        a.FullName,
                        a.AgentType,
                        a.Email,
                        a.MobileNumber,
                        a.LicenseNumber,
                        a.LicenseExpiryDate,
                        a.JoinedDate,
                        a.IsActive,
                        a.IdentityUserId,
                        IsLinkedToLogin = a.IdentityUserId != null
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading AgentProfiles");
                return GridError("Failed to load agent profiles.");
            }
        }

        // POST: /Security/AgentProfilesSave
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> AgentProfilesSave(AgentProfile model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.AgentCode) ||
                    string.IsNullOrWhiteSpace(model.FullName) ||
                    string.IsNullOrWhiteSpace(model.Email))
                {
                    return GridError("Agent Code, Full Name, and Email are required.");
                }

                if (model.AgentId == 0)
                {
                    var duplicate = await _db.AgentProfiles
                        .AnyAsync(a => a.AgentCode == model.AgentCode);
                    if (duplicate)
                        return GridError($"Agent Code '{model.AgentCode}' already exists.");

                    model.CreatedAt = DateTime.UtcNow;
                    _db.AgentProfiles.Add(model);
                }
                else
                {
                    var existing = await _db.AgentProfiles.FindAsync(model.AgentId);
                    if (existing == null) return GridError("Agent profile not found.");

                    existing.AgentCode = model.AgentCode;
                    existing.FullName = model.FullName;
                    existing.AgentType = model.AgentType;
                    existing.Email = model.Email;
                    existing.MobileNumber = model.MobileNumber;
                    existing.LicenseNumber = model.LicenseNumber;
                    existing.LicenseExpiryDate = model.LicenseExpiryDate;
                    existing.JoinedDate = model.JoinedDate;
                    existing.IsActive = model.IsActive;
                }

                await _db.SaveChangesAsync();
                return GridResult(new[] { model }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving AgentProfile");
                return GridError("Failed to save agent profile. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: SESSION AUDIT LOG
        // ════════════════════════════════════════════════════════════════

        // GET: /Security/AuditLog
        public IActionResult AuditLog() => View();

        // POST: /Security/AuditLogRead
        [HttpPost]
        public async Task<JsonResult> AuditLogRead(
            int page = 1, int pageSize = 20,
            string? searchText = null,
            string? activeOnly = null)
        {
            try
            {
                var query = _db.AuditSessionLogs.AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(l =>
                        l.Username.Contains(s) ||
                        l.IpAddress.Contains(s));
                }

                if (!string.IsNullOrWhiteSpace(activeOnly) && activeOnly == "Yes")
                    query = query.Where(l => l.IsActive);

                var total = await query.CountAsync();

                var data = await query
                    .OrderByDescending(l => l.LoginTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(l => new
                    {
                        l.SessionId,
                        l.Username,
                        l.IpAddress,
                        l.LoginTime,
                        l.LogoutTime,
                        l.IsActive,
                        l.LogoutReason,
                        Duration = l.LogoutTime.HasValue
                            ? EF.Functions.DateDiffMinute(l.LoginTime, l.LogoutTime.Value)
                            : (int?)null
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading AuditLog");
                return GridError("Failed to load audit log.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: LHDN E-INVOICE REGISTER (Feature 7)
        // ════════════════════════════════════════════════════════════════

        // GET: /Security/EInvoiceRegister
        public async Task<IActionResult> EInvoiceRegister()
        {
            var vm = new LhdnEInvoiceListViewModel
            {
                TotalInvoices = await _db.LhdnEInvoiceRecords.CountAsync(),
                ValidCount = await _db.LhdnEInvoiceRecords
                    .CountAsync(l => l.ValidationStatus == "Valid"),
                PendingCount = await _db.LhdnEInvoiceRecords
                    .CountAsync(l => l.ValidationStatus == "Pending"),
                RejectedCount = await _db.LhdnEInvoiceRecords
                    .CountAsync(l => l.ValidationStatus == "Rejected")
            };

            return View(vm);
        }

        // POST: /Security/EInvoiceRead
        [HttpPost]
        public async Task<JsonResult> EInvoiceRead(
            int page = 1, int pageSize = 15,
            string? searchText = null,
            string? status = null)
        {
            try
            {
                var query = _db.LhdnEInvoiceRecords
                    .Include(l => l.Policy)
                        .ThenInclude(p => p!.Client)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(l =>
                        l.InternalInvoiceNumber.Contains(s) ||
                        l.Policy!.CoverNoteNumber.Contains(s) ||
                        l.Policy!.Client!.ClientName.Contains(s) ||
                        (l.LhdnUniqueId != null && l.LhdnUniqueId.Contains(s)));
                }

                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                    query = query.Where(l => l.ValidationStatus == status);

                var total = await query.CountAsync();

                var data = await query
                    .OrderByDescending(l => l.TransmittedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(l => new
                    {
                        l.PolicyId,
                        CoverNoteNumber = l.Policy!.CoverNoteNumber,
                        ClientName = l.Policy!.Client!.ClientName,
                        l.InternalInvoiceNumber,
                        l.LhdnUniqueId,
                        l.TransmittedAt,
                        l.ValidationStatus,
                        l.IrbmErrorMessage,
                        NetPremiumPayable = _db.PremiumLedgers
                            .Where(pl => pl.PolicyId == l.PolicyId)
                            .Select(pl => (decimal?)pl.NetPremiumPayable)
                            .FirstOrDefault() ?? 0
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading EInvoice register");
                return GridError("Failed to load e-Invoice register.");
            }
        }

        // POST: /Security/EInvoiceUpdateStatus
        // Simulate updating the LHDN validation status (manual override for demo)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> EInvoiceUpdateStatus(
            int PolicyId, string ValidationStatus, string? IrbmErrorMessage)
        {
            try
            {
                var record = await _db.LhdnEInvoiceRecords.FindAsync(PolicyId);
                if (record == null) return GridError("e-Invoice record not found.");

                record.ValidationStatus = ValidationStatus;
                record.IrbmErrorMessage = IrbmErrorMessage;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Status updated." }, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating EInvoice status");
                return GridError("Failed to update. " + ex.Message);
            }
        }
    }
}