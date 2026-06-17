// Controllers/AuthController.cs
using ImsAgency.Web.Data;
using ImsAgency.Web.Models.Identity;
using ImsAgency.Web.Models.IMS;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ImsAgency.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace ImsAgency.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext db,
            ILogger<AccountController> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
            _logger = logger;
        }

        // ================================================================
        // GET: /Auth/Login
        // ================================================================
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // If the user is already logged in, don't show the login page again
            if (_signInManager.IsSignedIn(User))
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var model = new LoginViewModel { ReturnUrl = returnUrl };
            return View(model);
        }

        // ================================================================
        // POST: /Auth/Login
        // ================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Look up the user by username (NOT email, IMS Agency logs in by username)
            var user = await _userManager.FindByNameAsync(model.Username);

            if (user == null || !user.IsActive)
            {
                TempData["LoginError"] = "Invalid username or password.";
                return RedirectToAction("Login", new { returnUrl = model.ReturnUrl });
            }

            // PasswordSignInAsync checks the password AND handles lockout rules
            var result = await _signInManager.PasswordSignInAsync(
                user.UserName!,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                await WriteAuditLogAsync(user, isLogin: true);

                _logger.LogInformation("User {Username} logged in successfully.", user.UserName);

                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }

                return RedirectToAction("Index", "Dashboard");
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User {Username} account locked out.", user.UserName);
                TempData["LoginError"] = "Your account has been locked due to multiple failed attempts. Please try again later.";
            }
            else
            {
                TempData["LoginError"] = "Invalid username or password.";
            }

            return RedirectToAction("Login", new { returnUrl = model.ReturnUrl });
        }

        // ================================================================
        // POST: /Auth/Logout
        // ================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user != null)
            {
                await WriteAuditLogAsync(user, isLogin: false);
            }

            await _signInManager.SignOutAsync();

            _logger.LogInformation("User {Username} logged out.", user?.UserName ?? "Unknown");

           // return RedirectToAction("Login");
            return RedirectToAction("Login", "Account");
        }

        // ================================================================
        // GET: /Auth/AccessDenied
        // ================================================================
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ================================================================
        // Helper: Write to AuditSessionLogs (same pattern as MVA)
        // ================================================================
        private async Task WriteAuditLogAsync(ApplicationUser user, bool isLogin)
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

            if (isLogin)
            {
                var sessionLog = new AuditSessionLog
                {
                    UserId = user.Id,
                    Username = user.UserName ?? string.Empty,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    LoginTime = DateTime.UtcNow,
                    LogoutTime = null,
                    IsActive = true,
                    LogoutReason = null
                };

                _db.AuditSessionLogs.Add(sessionLog);
            }
            else
            {
                // Find the most recent OPEN session for this user and close it
                var openSession = _db.AuditSessionLogs
                    .Where(s => s.UserId == user.Id && s.IsActive)
                    .OrderByDescending(s => s.LoginTime)
                    .FirstOrDefault();

                if (openSession != null)
                {
                    openSession.LogoutTime = DateTime.UtcNow;
                    openSession.IsActive = false;
                    openSession.LogoutReason = "UserLogout";
                }
            }

            await _db.SaveChangesAsync();
        }


        // ─────────────────────────────────────────────────────────────────
        // GET /Account/ChangePassword
        // ─────────────────────────────────────────────────────────────────
        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View();
        }

        // ─────────────────────────────────────────────────────────────────
        // GET /Account/UserProfile
        // ─────────────────────────────────────────────────────────────────
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> UserProfile()
        {
            // Use Email claim instead of Name
            var email = User.FindFirstValue(ClaimTypes.Email);
            var user = await _userManager.FindByEmailAsync(email);

            string firstName = email;
            string lastName = string.Empty;

            if (email.Contains('.'))
            {
                var parts = email.Split('@')[0].Split('.');
                firstName = parts[0];
                lastName = parts.Length > 1 ? parts[1] : string.Empty;
            }

            var roles = user != null
                ? await _userManager.GetRolesAsync(user)
                : new List<string>();

            var model = new UserProfileView
            {
                UserName = user?.UserName ?? email,
                Email = user?.Email ?? email,
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = user?.PhoneNumber ?? string.Empty
            };

            return View(model);
        }




        // ─────────────────────────────────────────────────────────────────
        // GET /Account/Preferences
        // ─────────────────────────────────────────────────────────────────
        [Authorize]
        public IActionResult Preferences()
        {
            var model = new UserPreferences
            {
                Theme = Request.Cookies["UserTheme"] ?? "light",
                Language = Request.Cookies["UserLanguage"] ?? "en",
                EmailNotifications = Request.Cookies["EmailNotifications"] == "true",
                DefaultPageSize = int.TryParse(Request.Cookies["DefaultPageSize"], out int size) ? size : 50
            };
            return View(model);
        }

        // ─────────────────────────────────────────────────────────────────
        // POST /Account/SavePreferences
        // ─────────────────────────────────────────────────────────────────
        [Authorize]
        [HttpPost]
        public IActionResult SavePreferences([FromBody] UserPreferences model)
        {
            try
            {
                var options = new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = false
                };
                Response.Cookies.Append("UserTheme", model.Theme ?? "light", options);
                Response.Cookies.Append("UserLanguage", model.Language ?? "en", options);
                Response.Cookies.Append("EmailNotifications", model.EmailNotifications.ToString(), options);
                Response.Cookies.Append("DefaultPageSize", model.DefaultPageSize.ToString(), options);

                _logger.LogInformation("Preferences saved for user {User}", User.Identity?.Name);
                return Ok(new { success = true, message = "Preferences saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SavePreferences failed");
                return BadRequest(new { success = false, message = "Failed to save preferences." });
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────
        private IActionResult RedirectToLocal(string returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Dashboard");
        }
    }
}