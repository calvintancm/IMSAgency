// Controllers/AuthController.cs
using ImsAgency.Web.Data;
using ImsAgency.Web.ViewModels.Auth;
using ImsAgency.Web.Models.Identity;
using ImsAgency.Web.Models.IMS;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ImsAgency.Web.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext db,
            ILogger<AuthController> logger)
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
            return RedirectToAction("Login", "Auth");
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
    }
}