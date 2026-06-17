using ImsAgency.Web.Data;
using ImsAgency.Web.Models.IMS;
using ImsAgency.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize]
    public class ClaimController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ClaimController> _logger;

        public ClaimController(ApplicationDbContext db, ILogger<ClaimController> logger)
        {
            _db = db;
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


        // ================================================================
        // HELPER: Generate next ClaimReferenceNumber "CLM-2026-00004"
        // ================================================================
        private async Task<string> GenerateClaimRefAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"CLM-{year}-";

            var existingSeqs = await _db.Claims
                .Where(c => c.ClaimReferenceNumber.StartsWith(prefix))
                .Select(c => c.ClaimReferenceNumber.Substring(prefix.Length))
                .ToListAsync();

            int next = 1;
            if (existingSeqs.Any())
            {
                var max = existingSeqs
                    .Select(s => int.TryParse(s, out var n) ? n : 0)
                    .DefaultIfEmpty(0)
                    .Max();
                next = max + 1;
            }

            return $"{prefix}{next:D5}";
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: ALL CLAIMS
        // ════════════════════════════════════════════════════════════════

        // GET: /Claim/AllClaims
        public async Task<IActionResult> AllClaims()
        {
            var vm = new ClaimListViewModel
            {
                TotalClaims = await _db.Claims.CountAsync(),

                OpenClaims = await _db.Claims
                    .CountAsync(c => c.ClaimStatus == "Lodged"
                                  || c.ClaimStatus == "InProgress"),

                ApprovedClaims = await _db.Claims
                    .CountAsync(c => c.ClaimStatus == "Approved"),

                TotalApprovedAmount = await _db.Claims
                    .Where(c => c.ApprovedClaimAmount.HasValue)
                    .SumAsync(c => (decimal?)c.ApprovedClaimAmount) ?? 0
            };

            return View(vm);
        }

        // POST: /Claim/ClaimsRead
        [HttpPost]
        public async Task<JsonResult> ClaimsRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null,
            string? claimType = null,
            string? status = null)
        {
            try
            {
                var query = _db.Claims
                    .Include(c => c.Policy)
                        .ThenInclude(p => p!.Client)
                    .Include(c => c.Policy)
                        .ThenInclude(p => p!.Vehicle)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(c =>
                        c.ClaimReferenceNumber.Contains(s) ||
                        (c.InsurerClaimNumber != null && c.InsurerClaimNumber.Contains(s)) ||
                        c.Policy!.CoverNoteNumber.Contains(s) ||
                        c.Policy!.Client!.ClientName.Contains(s));
                }

                if (!string.IsNullOrWhiteSpace(claimType) && claimType != "All")
                    query = query.Where(c => c.ClaimType == claimType);

                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                    query = query.Where(c => c.ClaimStatus == status);

                var total = await query.CountAsync();

                bool desc = string.Equals(sortDir, "desc",
                    StringComparison.OrdinalIgnoreCase);

                query = sortField switch
                {
                    "ClaimDate" => desc
                        ? query.OrderByDescending(c => c.ClaimDate)
                        : query.OrderBy(c => c.ClaimDate),
                    "ClaimStatus" => desc
                        ? query.OrderByDescending(c => c.ClaimStatus)
                        : query.OrderBy(c => c.ClaimStatus),
                    _ => query.OrderByDescending(c => c.ClaimId)
                };

                var data = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(c => new
                    {
                        c.ClaimId,
                        c.ClaimReferenceNumber,
                        c.InsurerClaimNumber,
                        ClientName = c.Policy!.Client!.ClientName,
                        CoverNoteNumber = c.Policy!.CoverNoteNumber,
                        RegistrationNumber = c.Policy!.Vehicle != null
                            ? c.Policy!.Vehicle!.RegistrationNumber : null,
                        c.ClaimDate,
                        c.ClaimType,
                        c.ClaimStatus,
                        c.EstimatedLossAmount,
                        c.ApprovedClaimAmount,
                        c.WorkshopName,
                        DocumentCount = c.ClaimDocuments.Count()
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Claims grid");
                return GridError("Failed to load claims.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: PENDING CLAIMS
        // ════════════════════════════════════════════════════════════════

        // GET: /Claim/PendingClaims
        public IActionResult PendingClaims() => View();

        // POST: /Claim/PendingClaimsRead
        [HttpPost]
        public async Task<JsonResult> PendingClaimsRead(
            int page = 1, int pageSize = 15,
            string? searchText = null)
        {
            try
            {
                var query = _db.Claims
                    .Include(c => c.Policy)
                        .ThenInclude(p => p!.Client)
                    .Include(c => c.Policy)
                        .ThenInclude(p => p!.Vehicle)
                    .Where(c => c.ClaimStatus == "Lodged"
                             || c.ClaimStatus == "InProgress")
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(c =>
                        c.ClaimReferenceNumber.Contains(s) ||
                        c.Policy!.CoverNoteNumber.Contains(s) ||
                        c.Policy!.Client!.ClientName.Contains(s));
                }

                var total = await query.CountAsync();

                var data = await query
                    .OrderBy(c => c.ClaimDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(c => new
                    {
                        c.ClaimId,
                        c.ClaimReferenceNumber,
                        c.InsurerClaimNumber,
                        ClientName = c.Policy!.Client!.ClientName,
                        CoverNoteNumber = c.Policy!.CoverNoteNumber,
                        RegistrationNumber = c.Policy!.Vehicle != null
                            ? c.Policy!.Vehicle!.RegistrationNumber : null,
                        c.ClaimDate,
                        c.ReportedDate,
                        c.ClaimType,
                        c.ClaimStatus,
                        c.EstimatedLossAmount,
                        DocumentCount = c.ClaimDocuments.Count(),
                        DaysOpen = EF.Functions.DateDiffDay(c.ClaimDate, DateTime.UtcNow)
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading PendingClaims");
                return GridError("Failed to load pending claims.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: LODGE / EDIT CLAIM
        // ════════════════════════════════════════════════════════════════

        // GET: /Claim/LodgeClaim
        [Authorize(Roles = "Admin,SeniorAgent,Agent")]
        public IActionResult LodgeClaim()
        {
            return View("ClaimForm", new LodgeClaimViewModel());
        }

        // GET: /Claim/EditClaim/5
        [Authorize(Roles = "Admin,SeniorAgent,Agent")]
        public async Task<IActionResult> EditClaim(int id)
        {
            var claim = await _db.Claims
                .Include(c => c.Policy)
                    .ThenInclude(p => p!.Client)
                .Include(c => c.Policy)
                    .ThenInclude(p => p!.Vehicle)
                .FirstOrDefaultAsync(c => c.ClaimId == id);

            if (claim == null)
            {
                TempData["ErrorMessage"] = "Claim not found.";
                return RedirectToAction("AllClaims");
            }

            var vm = new LodgeClaimViewModel
            {
                ClaimId = claim.ClaimId,
                PolicyId = claim.PolicyId,
                PolicyDisplay = claim.Policy?.CoverNoteNumber
                    + (claim.Policy?.Vehicle != null
                        ? " — " + claim.Policy.Vehicle.RegistrationNumber : "")
                    + " (" + claim.Policy?.Client?.ClientName + ")",
                ClaimDate = claim.ClaimDate,
                ReportedDate = claim.ReportedDate,
                ClaimType = claim.ClaimType,
                ClaimStatus = claim.ClaimStatus,
                EstimatedLossAmount = claim.EstimatedLossAmount,
                ApprovedClaimAmount = claim.ApprovedClaimAmount,
                ClaimNarrative = claim.ClaimNarrative,
                WorkshopName = claim.WorkshopName,
                CloseDate = claim.CloseDate,
                Remarks = claim.Remarks
            };

            return View("ClaimForm", vm);
        }

        // POST: /Claim/SaveClaim
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SeniorAgent,Agent")]
        public async Task<JsonResult> SaveClaim(LodgeClaimViewModel model)
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
                Claim claim;

                if (model.ClaimId == 0)
                {
                    // ---- CREATE ----
                    var policyExists = await _db.Policies
                        .AnyAsync(p => p.PolicyId == model.PolicyId);
                    if (!policyExists)
                        return Json(new { success = false, message = "Policy not found." });

                    claim = new Claim
                    {
                        PolicyId = model.PolicyId,
                        ClaimReferenceNumber = await GenerateClaimRefAsync(),
                        ClaimDate = model.ClaimDate,
                        ReportedDate = model.ReportedDate,
                        ClaimType = model.ClaimType,
                        ClaimStatus = model.ClaimStatus,
                        EstimatedLossAmount = model.EstimatedLossAmount,
                        ApprovedClaimAmount = model.ApprovedClaimAmount,
                        ClaimNarrative = model.ClaimNarrative,
                        WorkshopName = model.WorkshopName,
                        CloseDate = model.CloseDate,
                        Remarks = model.Remarks,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = User.Identity?.Name
                    };

                    _db.Claims.Add(claim);
                }
                else
                {
                    // ---- UPDATE ----
                    claim = await _db.Claims.FindAsync(model.ClaimId)
                        ?? throw new Exception("Claim not found.");

                    claim.ClaimType = model.ClaimType;
                    claim.ClaimStatus = model.ClaimStatus;
                    claim.EstimatedLossAmount = model.EstimatedLossAmount;
                    claim.ApprovedClaimAmount = model.ApprovedClaimAmount;
                    claim.ClaimNarrative = model.ClaimNarrative;
                    claim.WorkshopName = model.WorkshopName;
                    claim.CloseDate = model.ClaimStatus == "Closed"
                        ? (model.CloseDate ?? DateTime.UtcNow)
                        : model.CloseDate;
                    claim.Remarks = model.Remarks;
                    claim.UpdatedBy = User.Identity?.Name;
                    claim.UpdatedAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    claimId = claim.ClaimId,
                    message = $"Claim {claim.ClaimReferenceNumber} saved successfully.",
                    redirectUrl = Url.Action("EditClaim", new { id = claim.ClaimId })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Claim");
                return Json(new { success = false, message = "Failed to save claim. " + ex.Message });
            }
        }

        // POST: /Claim/SearchActivePolicies?term=cn-2025
        [HttpGet]
        public async Task<JsonResult> SearchActivePolicies(string? term)
        {
            var query = _db.Policies
                .Include(p => p.Client)
                .Include(p => p.Vehicle)
                .Where(p => p.PolicyStatus == "Active");

            if (!string.IsNullOrWhiteSpace(term))
            {
                var t = term.Trim();
                query = query.Where(p =>
                    p.CoverNoteNumber.Contains(t) ||
                    p.Client!.ClientName.Contains(t) ||
                    (p.Vehicle != null && p.Vehicle.RegistrationNumber.Contains(t)));
            }

            var data = await query
                .OrderByDescending(p => p.PolicyId)
                .Take(20)
                .Select(p => new
                {
                    Id = p.PolicyId,
                    Text = p.CoverNoteNumber
                         + (p.Vehicle != null ? " — " + p.Vehicle.RegistrationNumber : "")
                         + " (" + p.Client!.ClientName + ")"
                })
                .ToListAsync();

            return Json(data);
        }
    }
}