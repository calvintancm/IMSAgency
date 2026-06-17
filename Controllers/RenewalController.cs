using ImsAgency.Web.Data;
using ImsAgency.Web.Models.IMS;
using ImsAgency.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize] // all roles can view renewals (Support, Agent, SeniorAgent, Admin)
    public class RenewalController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<RenewalController> _logger;

        public RenewalController(ApplicationDbContext db, ILogger<RenewalController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ================================================================
        // SHARED JSON HELPERS
        // ================================================================
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


        // ================================================================
        // HELPER: Core renewal query — active policies expiring within
        // a given day range, shared across all pipeline views
        // ================================================================
        private IQueryable<Policy> RenewalQuery(int? maxDays = null, int minDays = 0)
        {
            var today = DateTime.UtcNow.Date;
            var fromDate = today.AddDays(minDays);

            var query = _db.Policies
                .Include(p => p.Client)
                    .ThenInclude(c => c!.ClientPhones)
                .Include(p => p.Vehicle)
                .Include(p => p.Insurer)
                .Include(p => p.PolicyClass)
                .Include(p => p.PremiumLedger)
                .Where(p => p.PolicyStatus == "Active"
                         && p.ExpiryDate >= fromDate);

            if (maxDays.HasValue)
            {
                var toDate = today.AddDays(maxDays.Value);
                query = query.Where(p => p.ExpiryDate <= toDate);
            }

            return query;
        }


        // ================================================================
        // HELPER: Build the standard grid DTO from a policy
        // (computed in-memory since EF can't translate DaysToExpiry)
        // ================================================================
        private object BuildRenewalDto(Policy p)
        {
            var today = DateTime.UtcNow.Date;
            var primaryPhone = p.Client?.ClientPhones
                .FirstOrDefault(ph => ph.IsPrimary);

            return new
            {
                p.PolicyId,
                p.CoverNoteNumber,
                ClientName = p.Client?.ClientName ?? "",
                ClientId = p.Client?.ClientId ?? 0,
                RegistrationNumber = p.Vehicle?.RegistrationNumber,
                PolicyClassName = p.PolicyClass?.ClassName ?? "",
                InsurerName = p.Insurer?.InsurerName ?? "",
                p.ExpiryDate,
                DaysToExpiry = (p.ExpiryDate.Date - today).Days,
                NetPremiumPayable = p.PremiumLedger?.NetPremiumPayable ?? 0,
                p.RenewalReminderCount,
                p.RenewalReminderSentAt,
                PrimaryPhone = primaryPhone?.PhoneNumber,
                IsWhatsApp = primaryPhone?.IsWhatsApp ?? false,
                p.AgentCode
            };
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: DASHBOARD — All Renewals (within 90 days)
        // ════════════════════════════════════════════════════════════════

        // GET: /Renewal/AllRenewals
        public async Task<IActionResult> AllRenewals()
        {
            var today = DateTime.UtcNow.Date;

            var vm = new RenewalListViewModel
            {
                TotalDue7Days = await _db.Policies.CountAsync(p =>
                    p.PolicyStatus == "Active"
                    && p.ExpiryDate >= today
                    && p.ExpiryDate <= today.AddDays(7)),

                TotalDue30Days = await _db.Policies.CountAsync(p =>
                    p.PolicyStatus == "Active"
                    && p.ExpiryDate >= today
                    && p.ExpiryDate <= today.AddDays(30)),

                TotalDue60Days = await _db.Policies.CountAsync(p =>
                    p.PolicyStatus == "Active"
                    && p.ExpiryDate >= today
                    && p.ExpiryDate <= today.AddDays(60)),

                TotalDue90Days = await _db.Policies.CountAsync(p =>
                    p.PolicyStatus == "Active"
                    && p.ExpiryDate >= today
                    && p.ExpiryDate <= today.AddDays(90)),

                TotalExpired = await _db.Policies.CountAsync(p =>
                    p.PolicyStatus == "Expired"),

                TotalNoticesSentToday = await _db.RenewalNotices.CountAsync(n =>
                    n.SentAt.Date == today)
            };

            return View(vm);
        }

        // POST: /Renewal/AllRenewalsRead
        [HttpPost]
        public async Task<JsonResult> AllRenewalsRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null)
        {
            try
            {
                var query = RenewalQuery(maxDays: 90);

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(p =>
                        p.CoverNoteNumber.Contains(s) ||
                        p.Client!.ClientName.Contains(s) ||
                        (p.Vehicle != null && p.Vehicle.RegistrationNumber.Contains(s)));
                }

                var total = await query.CountAsync();

                bool desc = string.Equals(sortDir, "desc",
                    StringComparison.OrdinalIgnoreCase);

                query = sortField switch
                {
                    "ExpiryDate" => desc
                        ? query.OrderByDescending(p => p.ExpiryDate)
                        : query.OrderBy(p => p.ExpiryDate),
                    "ClientName" => desc
                        ? query.OrderByDescending(p => p.Client!.ClientName)
                        : query.OrderBy(p => p.Client!.ClientName),
                    _ => query.OrderBy(p => p.ExpiryDate) // default: soonest first
                };

                var policies = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var data = policies.Select(p => BuildRenewalDto(p));
                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading AllRenewals");
                return GridError("Failed to load renewals.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: DUE IN 30 / 60 / 90 DAYS (shared grid read action)
        // ════════════════════════════════════════════════════════════════

        // GET: /Renewal/Due30
        public IActionResult Due30() => View("RenewalBucket",
            new RenewalBucketConfig(30, "Expiring in 30 Days"));

        // GET: /Renewal/Due60
        public IActionResult Due60() => View("RenewalBucket",
            new RenewalBucketConfig(60, "Expiring in 30–60 Days"));

        // GET: /Renewal/Due90
        public IActionResult Due90() => View("RenewalBucket",
            new RenewalBucketConfig(90, "Expiring in 60–90 Days"));

        // POST: /Renewal/BucketRead?maxDays=30
        [HttpPost]
        public async Task<JsonResult> BucketRead(
            int maxDays = 30, int minDays = 0,
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null)
        {
            try
            {
                var query = RenewalQuery(maxDays: maxDays, minDays: minDays);

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(p =>
                        p.CoverNoteNumber.Contains(s) ||
                        p.Client!.ClientName.Contains(s) ||
                        (p.Vehicle != null && p.Vehicle.RegistrationNumber.Contains(s)));
                }

                var total = await query.CountAsync();

                query = query.OrderBy(p => p.ExpiryDate); // always soonest first

                var policies = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var data = policies.Select(p => BuildRenewalDto(p));
                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading BucketRead maxDays={MaxDays}", maxDays);
                return GridError("Failed to load renewal pipeline.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: RENEWAL NOTICES LOG
        // ════════════════════════════════════════════════════════════════

        // GET: /Renewal/NoticesLog
        public IActionResult NoticesLog() => View();

        // POST: /Renewal/NoticesLogRead
        [HttpPost]
        public async Task<JsonResult> NoticesLogRead(
            int page = 1, int pageSize = 15,
            string? searchText = null,
            string? channel = null,
            string? noticeType = null)
        {
            try
            {
                var query = _db.RenewalNotices
                    .Include(n => n.Policy)
                        .ThenInclude(p => p!.Client)
                    .Include(n => n.Policy)
                        .ThenInclude(p => p!.Vehicle)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(n =>
                        n.Policy!.CoverNoteNumber.Contains(s) ||
                        n.Policy!.Client!.ClientName.Contains(s) ||
                        n.PhoneOrEmail.Contains(s));
                }

                if (!string.IsNullOrWhiteSpace(channel) && channel != "All")
                    query = query.Where(n => n.Channel == channel);

                if (!string.IsNullOrWhiteSpace(noticeType) && noticeType != "All")
                    query = query.Where(n => n.NoticeType == noticeType);

                var total = await query.CountAsync();

                var data = await query
                    .OrderByDescending(n => n.SentAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(n => new
                    {
                        n.NoticeId,
                        n.PolicyId,
                        CoverNoteNumber = n.Policy!.CoverNoteNumber,
                        ClientName = n.Policy!.Client!.ClientName,
                        RegistrationNumber = n.Policy!.Vehicle != null
                            ? n.Policy!.Vehicle.RegistrationNumber : null,
                        n.NoticeType,
                        n.SentAt,
                        n.Channel,
                        n.PhoneOrEmail,
                        n.MessageContent,
                        n.IsDelivered,
                        n.DeliveredAt,
                        n.AgentNote
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading NoticesLog");
                return GridError("Failed to load notices log.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: WHATSAPP REMINDER SIMULATOR (Feature 6)
        // ════════════════════════════════════════════════════════════════

        // POST: /Renewal/GetReminderPreview
        // Returns the pre-filled WhatsApp message so the UI can show a
        // preview popup BEFORE the agent confirms the send.
        [HttpPost]
        public async Task<JsonResult> GetReminderPreview(int policyId)
        {
            try
            {
                var policy = await _db.Policies
                    .Include(p => p.Client)
                        .ThenInclude(c => c!.ClientPhones)
                    .Include(p => p.Vehicle)
                    .Include(p => p.Agent)
                    .FirstOrDefaultAsync(p => p.PolicyId == policyId);

                if (policy == null)
                    return GridError("Policy not found.");

                var today = DateTime.UtcNow.Date;
                var daysLeft = (policy.ExpiryDate.Date - today).Days;

                // Determine the most appropriate NoticeType bucket
                var noticeType = daysLeft <= 7 ? "7Day"
                               : daysLeft <= 30 ? "30Day"
                               : daysLeft <= 60 ? "60Day"
                               : "90Day";

                var primaryPhone = policy.Client?.ClientPhones
                    .FirstOrDefault(p => p.IsPrimary);

                var agentName = policy.Agent?.FullName
                    ?? User.Identity?.Name
                    ?? "IMS Agency";

                var agentPhone = policy.Agent?.MobileNumber ?? "";

                var vehicleRef = policy.Vehicle != null
                    ? $"vehicle {policy.Vehicle.RegistrationNumber}"
                    : $"policy {policy.CoverNoteNumber}";

                var expiryStr = policy.ExpiryDate.ToString("dd/MM/yyyy");

                var message =
                    $"Dear {policy.Client?.ClientName},\n\n" +
                    $"Your insurance policy *{policy.CoverNoteNumber}* for " +
                    $"your {vehicleRef} will expire on *{expiryStr}* " +
                    $"({daysLeft} day{(daysLeft == 1 ? "" : "s")} remaining).\n\n" +
                    $"Please contact us at your earliest convenience to arrange renewal " +
                    $"and avoid a lapse in coverage.\n\n" +
                    $"Agent: {agentName}\n" +
                    $"Tel: {agentPhone}\n\n" +
                    $"Thank you — IMS Agency\n" +
                    $"\"Renew Smarter. Serve Better.\"";

                return Json(new
                {
                    success = true,
                    noticeType,
                    daysLeft,
                    phoneOrEmail = primaryPhone?.PhoneNumber ?? "",
                    isWhatsApp = primaryPhone?.IsWhatsApp ?? false,
                    message
                }, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building reminder preview for Policy {Id}", policyId);
                return GridError("Failed to build reminder preview.");
            }
        }

        // POST: /Renewal/SendReminder (Feature 6)
        // Logs the simulated send to RenewalNotices — no actual API call yet.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SendReminder(SendReminderViewModel model)
        {
            try
            {
                if (model.PolicyId == 0 || string.IsNullOrWhiteSpace(model.PhoneOrEmail))
                    return GridError("Missing required fields.");

                var policy = await _db.Policies.FindAsync(model.PolicyId);
                if (policy == null) return GridError("Policy not found.");

                // Write the notice log row
                var notice = new RenewalNotice
                {
                    PolicyId = model.PolicyId,
                    NoticeType = model.NoticeType,
                    SentAt = DateTime.UtcNow,
                    Channel = model.Channel,
                    PhoneOrEmail = model.PhoneOrEmail,
                    MessageContent = model.MessageContent,
                    IsDelivered = true, // simulated — assumed delivered
                    DeliveredAt = DateTime.UtcNow,
                    AgentNote = model.AgentNote
                };

                _db.RenewalNotices.Add(notice);

                // Update the policy's reminder tracking fields
                policy.RenewalReminderSentAt = DateTime.UtcNow;
                policy.RenewalReminderCount += 1;

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Reminder logged as sent via {model.Channel} to {model.PhoneOrEmail}.",
                    newReminderCount = policy.RenewalReminderCount
                }, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending reminder for Policy {Id}", model.PolicyId);
                return GridError("Failed to log reminder. " + ex.Message);
            }
        }
    }

    // Config record passed to the shared RenewalBucket view
    public record RenewalBucketConfig(int MaxDays, string Title);
}