using ImsAgency.Web.Data;
using ImsAgency.Web.Models.IMS;
using ImsAgency.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize(Roles = "Admin,SeniorAgent,Agent")] // matches sidebar: isAgent gating
    public class PolicyController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<PolicyController> _logger;

        public PolicyController(ApplicationDbContext db, ILogger<PolicyController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ================================================================
        // SHARED JSON HELPERS
        // ================================================================
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        };

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
        // HELPER: Load filter dropdowns (Insurers + Motor Policy Classes)
        // ================================================================
        private async Task LoadFilterDropdownsAsync()
        {
            ViewBag.Insurers = await _db.Insurers
                .Where(i => i.IsActive && (i.InsurerType == "Motor" || i.InsurerType == "Both"))
                .OrderBy(i => i.InsurerName)
                .Select(i => new { i.InsurerId, i.InsurerName })
                .ToListAsync();

            ViewBag.MotorPolicyClasses = await _db.PolicyClasses
                .Where(pc => pc.IsActive && pc.ClassCategory == "Motor")
                .OrderBy(pc => pc.DisplayOrder)
                .Select(pc => new { pc.ClassCode, pc.ClassName })
                .ToListAsync();
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: ALL MOTOR POLICIES
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/MotorPolicies
        public async Task<IActionResult> MotorPolicies()
        {
            var today = DateTime.UtcNow.Date;

            var vm = new PolicyListViewModel
            {
                TotalMotorPolicies = await _db.Policies
                    .CountAsync(p => p.PolicyClass!.ClassCategory == "Motor"),

                ActiveMotorPolicies = await _db.Policies
                    .CountAsync(p => p.PolicyClass!.ClassCategory == "Motor" && p.PolicyStatus == "Active"),

                ExpiringIn30Days = await _db.Policies
                    .CountAsync(p => p.PolicyClass!.ClassCategory == "Motor"
                                   && p.PolicyStatus == "Active"
                                   && p.ExpiryDate >= today && p.ExpiryDate <= today.AddDays(30)),

                TotalSumInsured = await _db.Policies
                    .Where(p => p.PolicyClass!.ClassCategory == "Motor" && p.PolicyStatus == "Active")
                    .SumAsync(p => (decimal?)p.SumInsured) ?? 0
            };

            await LoadFilterDropdownsAsync();
            return View(vm);
        }

        // POST: /Policy/MotorPoliciesRead
        [HttpPost]
        public async Task<JsonResult> MotorPoliciesRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null, int? insurerId = null,
            string? policyClassCode = null, string? status = null)
        {
            try
            {
                var today = DateTime.UtcNow.Date;

                var query = _db.Policies
                    .Include(p => p.Client)
                    .Include(p => p.Vehicle)
                    .Include(p => p.Insurer)
                    .Include(p => p.PolicyClass)
                    .Include(p => p.PremiumLedger)
                    .Where(p => p.PolicyClass!.ClassCategory == "Motor")
                    .AsQueryable();

                // ---- Free-text search: Cover Note / Policy No. / Client / Reg No. ----
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(p =>
                        p.CoverNoteNumber.Contains(s) ||
                        (p.PolicyNumber != null && p.PolicyNumber.Contains(s)) ||
                        p.Client!.ClientName.Contains(s) ||
                        (p.Vehicle != null && p.Vehicle.RegistrationNumber.Contains(s)));
                }

                // ---- Insurer filter ----
                if (insurerId.HasValue && insurerId.Value > 0)
                {
                    query = query.Where(p => p.InsurerId == insurerId.Value);
                }

                // ---- Policy Class filter ----
                if (!string.IsNullOrWhiteSpace(policyClassCode) && policyClassCode != "All")
                {
                    query = query.Where(p => p.PolicyClassCode == policyClassCode);
                }

                // ---- Status filter ----
                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                {
                    query = query.Where(p => p.PolicyStatus == status);
                }

                var total = await query.CountAsync();

                // ---- Sorting ----
                bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
                query = sortField switch
                {
                    "CoverNoteNumber" => desc ? query.OrderByDescending(p => p.CoverNoteNumber) : query.OrderBy(p => p.CoverNoteNumber),
                    "ClientName" => desc ? query.OrderByDescending(p => p.Client!.ClientName) : query.OrderBy(p => p.Client!.ClientName),
                    "ExpiryDate" => desc ? query.OrderByDescending(p => p.ExpiryDate) : query.OrderBy(p => p.ExpiryDate),
                    "StartDate" => desc ? query.OrderByDescending(p => p.StartDate) : query.OrderBy(p => p.StartDate),
                    _ => query.OrderByDescending(p => p.PolicyId) // default: newest first
                };

                var rawData = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.PolicyId,
                        p.CoverNoteNumber,
                        p.PolicyNumber,
                        ClientName = p.Client!.ClientName,
                        RegistrationNumber = p.Vehicle != null ? p.Vehicle.RegistrationNumber : null,
                        InsurerName = p.Insurer!.InsurerName,
                        PolicyClassName = p.PolicyClass!.ClassName,
                        p.PolicyStatus,
                        p.StartDate,
                        p.ExpiryDate,
                        NetPremiumPayable = p.PremiumLedger != null ? p.PremiumLedger.NetPremiumPayable : 0,
                        p.IsRenewal
                    })
                    .ToListAsync();

                // DaysToExpiry computed in-memory (cleaner than DB-translated DateTime math)
                var data = rawData.Select(p => new
                {
                    p.PolicyId,
                    p.CoverNoteNumber,
                    p.PolicyNumber,
                    p.ClientName,
                    p.RegistrationNumber,
                    p.InsurerName,
                    p.PolicyClassName,
                    p.PolicyStatus,
                    p.StartDate,
                    p.ExpiryDate,
                    p.NetPremiumPayable,
                    p.IsRenewal,
                    DaysToExpiry = (p.ExpiryDate.Date - today).Days
                });

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading MotorPolicies grid");
                return GridError("Failed to load motor policies.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: EXPIRED MOTOR POLICIES
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/ExpiredMotor
        public async Task<IActionResult> ExpiredMotor()
        {
            var vm = new ExpiredPolicyListViewModel
            {
                TotalExpiredMotor = await _db.Policies
                    .CountAsync(p => p.PolicyClass!.ClassCategory == "Motor" && p.PolicyStatus == "Expired"),

                // Count expired policies that already have a renewal quote
                // pointing back at them via PreviousPolicyId
                PendingRenewalQuotes = await _db.Policies
                    .CountAsync(p => p.PolicyClass!.ClassCategory == "Motor"
                                   && p.PolicyStatus == "Expired"
                                   && _db.Policies.Any(q => q.PreviousPolicyId == p.PolicyId))
            };

            await LoadFilterDropdownsAsync();
            return View(vm);
        }

        // POST: /Policy/ExpiredMotorRead
        [HttpPost]
        public async Task<JsonResult> ExpiredMotorRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null, int? insurerId = null,
            string? policyClassCode = null)
        {
            try
            {
                var query = _db.Policies
                    .Include(p => p.Client)
                    .Include(p => p.Vehicle)
                    .Include(p => p.Insurer)
                    .Include(p => p.PolicyClass)
                    .Include(p => p.PremiumLedger)
                    .Where(p => p.PolicyClass!.ClassCategory == "Motor" && p.PolicyStatus == "Expired")
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(p =>
                        p.CoverNoteNumber.Contains(s) ||
                        (p.PolicyNumber != null && p.PolicyNumber.Contains(s)) ||
                        p.Client!.ClientName.Contains(s) ||
                        (p.Vehicle != null && p.Vehicle.RegistrationNumber.Contains(s)));
                }

                if (insurerId.HasValue && insurerId.Value > 0)
                {
                    query = query.Where(p => p.InsurerId == insurerId.Value);
                }

                if (!string.IsNullOrWhiteSpace(policyClassCode) && policyClassCode != "All")
                {
                    query = query.Where(p => p.PolicyClassCode == policyClassCode);
                }

                var total = await query.CountAsync();

                bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
                query = sortField switch
                {
                    "CoverNoteNumber" => desc ? query.OrderByDescending(p => p.CoverNoteNumber) : query.OrderBy(p => p.CoverNoteNumber),
                    "ClientName" => desc ? query.OrderByDescending(p => p.Client!.ClientName) : query.OrderBy(p => p.Client!.ClientName),
                    "ExpiryDate" => desc ? query.OrderByDescending(p => p.ExpiryDate) : query.OrderBy(p => p.ExpiryDate),
                    _ => query.OrderByDescending(p => p.ExpiryDate) // default: most recently expired first
                };

                var data = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.PolicyId,
                        p.CoverNoteNumber,
                        p.PolicyNumber,
                        ClientName = p.Client!.ClientName,
                        RegistrationNumber = p.Vehicle != null ? p.Vehicle.RegistrationNumber : null,
                        InsurerName = p.Insurer!.InsurerName,
                        PolicyClassName = p.PolicyClass!.ClassName,
                        p.ExpiryDate,
                        NetPremiumPayable = p.PremiumLedger != null ? p.PremiumLedger.NetPremiumPayable : 0,
                        HasRenewalQuote = _db.Policies.Any(q => q.PreviousPolicyId == p.PolicyId)
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading ExpiredMotor grid");
                return GridError("Failed to load expired motor policies.");
            }
        }

        // ================================================================
        // HELPER: Generate next CoverNoteNumber "CN-2026-10015"
        // ================================================================
        private async Task<string> GenerateCoverNoteNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"CN-{year}-";

            var existingSeqs = await _db.Policies
                .Where(p => p.CoverNoteNumber.StartsWith(prefix))
                .Select(p => p.CoverNoteNumber.Substring(prefix.Length))
                .ToListAsync();

            int nextSeq = 10001;
            if (existingSeqs.Any())
            {
                var maxSeq = existingSeqs
                    .Select(s => int.TryParse(s, out var n) ? n : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                nextSeq = Math.Max(maxSeq + 1, 10001);
            }

            return $"{prefix}{nextSeq}";
        }

        // ================================================================
        // HELPER: Load all dropdowns needed by the Motor Policy form
        // ================================================================
        private async Task LoadMotorPolicyDropdownsAsync()
        {
            ViewBag.Insurers = await _db.Insurers
                .Where(i => i.IsActive && (i.InsurerType == "Motor" || i.InsurerType == "Both"))
                .OrderBy(i => i.InsurerName)
                .Select(i => new { i.InsurerId, i.InsurerName })
                .ToListAsync();

            ViewBag.MotorPolicyClasses = await _db.PolicyClasses
                .Where(pc => pc.IsActive && pc.ClassCategory == "Motor")
                .OrderBy(pc => pc.DisplayOrder)
                .Select(pc => new { pc.ClassCode, pc.ClassName })
                .ToListAsync();

            ViewBag.NcdRates = await _db.NcdRates
                .OrderBy(n => n.ClaimFreeYears)
                .Select(n => new { n.ClaimFreeYears, n.NcdPercentage })
                .Distinct()
                .ToListAsync();

            ViewBag.Agents = await _db.AgentProfiles
                .Where(a => a.IsActive)
                .OrderBy(a => a.FullName)
                .Select(a => new { a.AgentCode, a.FullName })
                .ToListAsync();
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: NEW / EDIT MOTOR POLICY (shared form)
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/NewMotorPolicy
        public async Task<IActionResult> NewMotorPolicy()
        {
            await LoadMotorPolicyDropdownsAsync();

            var vm = new MotorPolicyFormViewModel
            {
                StampDutyAmount = 10.00m
            };

            return View("MotorPolicyForm", vm);
        }

        // GET: /Policy/EditMotorPolicy/5
        public async Task<IActionResult> EditMotorPolicy(int id)
        {
            var policy = await _db.Policies
                .Include(p => p.Client)
                .Include(p => p.Vehicle)
                .Include(p => p.PolicyClass)
                .Include(p => p.PremiumLedger)
                .FirstOrDefaultAsync(p => p.PolicyId == id);

            if (policy == null)
            {
                TempData["ErrorMessage"] = "Policy not found.";
                return RedirectToAction("MotorPolicies");
            }

            if (policy.PolicyClass?.ClassCategory != "Motor")
            {
                TempData["ErrorMessage"] = "This policy is not a motor policy.";
                return RedirectToAction("NonMotorPolicies");
            }

            await LoadMotorPolicyDropdownsAsync();

            var ledger = policy.PremiumLedger;

            var vm = new MotorPolicyFormViewModel
            {
                PolicyId = policy.PolicyId,
                CoverNoteNumber = policy.CoverNoteNumber,
                PolicyNumber = policy.PolicyNumber,
                ClientId = policy.ClientId,
                ClientDisplay = $"{policy.Client!.ClientCode} - {policy.Client.ClientName}",
                VehicleId = policy.VehicleId ?? 0,
                VehicleDisplay = policy.Vehicle != null
                    ? $"{policy.Vehicle.RegistrationNumber} - {policy.Vehicle.MakeAndModel}"
                    : null,
                InsurerId = policy.InsurerId,
                PolicyClassCode = policy.PolicyClassCode,
                PolicyStatus = policy.PolicyStatus,
                StartDate = policy.StartDate,
                ExpiryDate = policy.ExpiryDate,
                NcdPercentage = policy.NcdPercentage,
                SumInsured = policy.SumInsured,
                AgentCode = policy.AgentCode,
                Remarks = policy.Remarks,
                IsElectricVehicle = policy.Vehicle?.IsElectricVehicle ?? false,

                GrossPremium = ledger?.GrossPremium ?? 0,
                AddonWindscreen = ledger?.AddonWindscreen ?? 0,
                AddonSpecialPerils = ledger?.AddonSpecialPerils ?? 0,
                AddonNamedDriver = ledger?.AddonNamedDriver ?? 0,
                AddonTotalLoss = ledger?.AddonTotalLoss ?? 0,
                AddonEvCharger = ledger?.AddonEvCharger ?? 0,
                AgentCommission = ledger?.AgentCommission,

                NcdDiscountAmount = ledger?.NcdDiscountAmount ?? 0,
                NetPremium = ledger?.NetPremium ?? 0,
                TotalAddonAmount = ledger?.TotalAddonAmount ?? 0,
                ServiceTaxAmount = ledger?.ServiceTaxAmount ?? 0,
                StampDutyAmount = ledger?.StampDutyAmount ?? 10.00m,
                NetPremiumPayable = ledger?.NetPremiumPayable ?? 0
            };

            return View("MotorPolicyForm", vm);
        }

        // POST: /Policy/SaveMotorPolicy
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SaveMotorPolicy(MotorPolicyFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var firstError = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Validation failed.";

                return Json(new { success = false, message = firstError });
            }

            // ---- Cross-field validation ----
            if (model.ExpiryDate <= model.StartDate)
            {
                return Json(new { success = false, message = "Expiry Date must be after Start Date." });
            }

            try
            {
                var clientExists = await _db.Clients.AnyAsync(c => c.ClientId == model.ClientId);
                if (!clientExists) return Json(new { success = false, message = "Selected client not found." });

                var vehicleExists = await _db.Vehicles.AnyAsync(v => v.VehicleId == model.VehicleId);
                if (!vehicleExists) return Json(new { success = false, message = "Selected vehicle not found." });

                var insurerExists = await _db.Insurers.AnyAsync(i => i.InsurerId == model.InsurerId);
                if (!insurerExists) return Json(new { success = false, message = "Selected insurer not found." });

                Policy policy;

                if (model.PolicyId == 0)
                {
                    // ---- CREATE ----
                    policy = new Policy
                    {
                        CoverNoteNumber = await GenerateCoverNoteNumberAsync(),
                        PolicyNumber = model.PolicyNumber,
                        ClientId = model.ClientId,
                        VehicleId = model.VehicleId,
                        InsurerId = model.InsurerId,
                        PolicyClassCode = model.PolicyClassCode,
                        PolicyStatus = model.PolicyStatus,
                        StartDate = model.StartDate,
                        ExpiryDate = model.ExpiryDate,
                        NcdPercentage = model.NcdPercentage,
                        SumInsured = model.SumInsured,
                        AgentCode = string.IsNullOrWhiteSpace(model.AgentCode) ? null : model.AgentCode,
                        Remarks = model.Remarks,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = User.Identity?.Name
                    };

                    _db.Policies.Add(policy);
                    await _db.SaveChangesAsync(); // generates PolicyId for the PremiumLedger FK
                }
                else
                {
                    // ---- UPDATE ----
                    var existing = await _db.Policies
                        .Include(p => p.PremiumLedger)
                        .FirstOrDefaultAsync(p => p.PolicyId == model.PolicyId);

                    if (existing == null)
                    {
                        return Json(new { success = false, message = "Policy not found." });
                    }

                    existing.PolicyNumber = model.PolicyNumber;
                    existing.ClientId = model.ClientId;
                    existing.VehicleId = model.VehicleId;
                    existing.InsurerId = model.InsurerId;
                    existing.PolicyClassCode = model.PolicyClassCode;
                    existing.PolicyStatus = model.PolicyStatus;
                    existing.StartDate = model.StartDate;
                    existing.ExpiryDate = model.ExpiryDate;
                    existing.NcdPercentage = model.NcdPercentage;
                    existing.SumInsured = model.SumInsured;
                    existing.AgentCode = string.IsNullOrWhiteSpace(model.AgentCode) ? null : model.AgentCode;
                    existing.Remarks = model.Remarks;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.UpdatedBy = User.Identity?.Name;

                    policy = existing;
                }

                // ---- PREMIUM LEDGER: create or update, then recalculate
                //      server-side using PremiumLedger.RecalculateTotals()
                //      so the database NEVER trusts client-side math alone. ----
                var ledger = await _db.PremiumLedgers.FindAsync(policy.PolicyId);
                if (ledger == null)
                {
                    ledger = new PremiumLedger { PolicyId = policy.PolicyId };
                    _db.PremiumLedgers.Add(ledger);
                }

                ledger.SumInsuredAmount = model.SumInsured;
                ledger.GrossPremium = model.GrossPremium;
                ledger.NcdDiscountAmount = Math.Round(model.GrossPremium * model.NcdPercentage / 100m, 2);
                ledger.NetPremium = ledger.GrossPremium - ledger.NcdDiscountAmount;
                ledger.AddonWindscreen = model.AddonWindscreen;
                ledger.AddonSpecialPerils = model.AddonSpecialPerils;
                ledger.AddonNamedDriver = model.AddonNamedDriver;
                ledger.AddonTotalLoss = model.AddonTotalLoss;
                ledger.AddonEvCharger = model.AddonEvCharger;
                ledger.AgentCommission = model.AgentCommission;
                ledger.StampDutyAmount = 10.00m; // fixed by Malaysian law

                ledger.RecalculateTotals();

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    isNew = model.PolicyId == 0,
                    policyId = policy.PolicyId,
                    message = $"Policy {policy.CoverNoteNumber} saved successfully.",
                    redirectUrl = Url.Action("Details", new { id = policy.PolicyId })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Motor Policy");
                return Json(new { success = false, message = "Failed to save policy. " + ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: CLIENT / VEHICLE SEARCH (Kendo ComboBox remote data)
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/SearchClients?term=ahmad
        [HttpGet]
        public async Task<JsonResult> SearchClients(string? term)
        {
            var query = _db.Clients.Where(c => c.IsActive && !c.IsBlacklisted);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var t = term.Trim();
                query = query.Where(c =>
                    c.ClientName.Contains(t) ||
                    c.ClientCode.Contains(t) ||
                    c.IdentificationNumber.Contains(t));
            }

            var data = await query
                .OrderBy(c => c.ClientName)
                .Take(20)
                .Select(c => new
                {
                    Id = c.ClientId,
                    Text = c.ClientCode + " - " + c.ClientName
                })
                .ToListAsync();

            return Json(data);
        }

        // GET: /Policy/SearchVehicles?term=wxy
        [HttpGet]
        public async Task<JsonResult> SearchVehicles(string? term)
        {
            var query = _db.Vehicles.Where(v => v.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var t = term.Trim();
                query = query.Where(v =>
                    v.RegistrationNumber.Contains(t) ||
                    v.MakeAndModel.Contains(t));
            }

            var data = await query
                .OrderBy(v => v.RegistrationNumber)
                .Take(20)
                .Select(v => new
                {
                    Id = v.VehicleId,
                    Text = v.RegistrationNumber + " - " + v.MakeAndModel,
                    v.IsElectricVehicle,
                    v.MarketValue
                })
                .ToListAsync();

            return Json(data);
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: POLICY DETAILS (read-only)
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var policy = await _db.Policies
                .Include(p => p.Client)
                .Include(p => p.Vehicle)
                .Include(p => p.Insurer)
                .Include(p => p.PolicyClass)
                .Include(p => p.PremiumLedger)
                .Include(p => p.Agent)
                .FirstOrDefaultAsync(p => p.PolicyId == id);

            if (policy == null)
            {
                TempData["ErrorMessage"] = "Policy not found.";
                return RedirectToAction("MotorPolicies");
            }

            return View(policy);
        }
    }
}