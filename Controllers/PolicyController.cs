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



        //NON-MOTOR

        // ================================================================
        // HELPER: Load Non-Motor dropdowns
        // ================================================================
        private async Task LoadNonMotorDropdownsAsync()
        {
            ViewBag.Insurers = await _db.Insurers
                .Where(i => i.IsActive &&
                           (i.InsurerType == "NonMotor" || i.InsurerType == "Both"))
                .OrderBy(i => i.InsurerName)
                .Select(i => new { i.InsurerId, i.InsurerName })
                .ToListAsync();

            ViewBag.NonMotorPolicyClasses = await _db.PolicyClasses
                .Where(pc => pc.IsActive && pc.ClassCategory == "NonMotor")
                .OrderBy(pc => pc.DisplayOrder)
                .Select(pc => new { pc.ClassCode, pc.ClassName })
                .ToListAsync();

            ViewBag.Agents = await _db.AgentProfiles
                .Where(a => a.IsActive)
                .OrderBy(a => a.FullName)
                .Select(a => new { a.AgentCode, a.FullName })
                .ToListAsync();
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: ALL NON-MOTOR POLICIES
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/NonMotorPolicies
        public async Task<IActionResult> NonMotorPolicies()
        {
            var vm = new NonMotorPolicyListViewModel
            {
                TotalNonMotorPolicies = await _db.Policies
                    .CountAsync(p => p.PolicyClass!.ClassCategory == "NonMotor"),

                ActiveNonMotorPolicies = await _db.Policies
                    .CountAsync(p => p.PolicyClass!.ClassCategory == "NonMotor"
                                   && p.PolicyStatus == "Active"),

                GroupPolicies = await _db.Policies
                    .CountAsync(p => p.PolicyStatus == "Active"
                                   && (p.PolicyClassCode == "FWHS"
                                    || p.PolicyClassCode == "PA"
                                    || p.PolicyClassCode == "MEDHLT")),

                TotalSumInsured = await _db.Policies
                    .Where(p => p.PolicyClass!.ClassCategory == "NonMotor"
                               && p.PolicyStatus == "Active")
                    .SumAsync(p => (decimal?)p.SumInsured) ?? 0
            };

            await LoadNonMotorDropdownsAsync();
            return View(vm);
        }

        // POST: /Policy/NonMotorPoliciesRead
        [HttpPost]
        public async Task<JsonResult> NonMotorPoliciesRead(
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
                    .Include(p => p.Insurer)
                    .Include(p => p.PolicyClass)
                    .Include(p => p.PremiumLedger)
                    .Where(p => p.PolicyClass!.ClassCategory == "NonMotor")
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(p =>
                        p.CoverNoteNumber.Contains(s) ||
                        (p.PolicyNumber != null && p.PolicyNumber.Contains(s)) ||
                        p.Client!.ClientName.Contains(s));
                }

                if (insurerId.HasValue && insurerId.Value > 0)
                    query = query.Where(p => p.InsurerId == insurerId.Value);

                if (!string.IsNullOrWhiteSpace(policyClassCode) && policyClassCode != "All")
                    query = query.Where(p => p.PolicyClassCode == policyClassCode);

                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                    query = query.Where(p => p.PolicyStatus == status);

                var total = await query.CountAsync();

                bool desc = string.Equals(sortDir, "desc",
                    StringComparison.OrdinalIgnoreCase);

                query = sortField switch
                {
                    "CoverNoteNumber" => desc
                        ? query.OrderByDescending(p => p.CoverNoteNumber)
                        : query.OrderBy(p => p.CoverNoteNumber),
                    "ClientName" => desc
                        ? query.OrderByDescending(p => p.Client!.ClientName)
                        : query.OrderBy(p => p.Client!.ClientName),
                    "ExpiryDate" => desc
                        ? query.OrderByDescending(p => p.ExpiryDate)
                        : query.OrderBy(p => p.ExpiryDate),
                    _ => query.OrderByDescending(p => p.PolicyId)
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
                        InsurerName = p.Insurer!.InsurerName,
                        PolicyClassName = p.PolicyClass!.ClassName,
                        p.PolicyClassCode,
                        p.PolicyStatus,
                        p.StartDate,
                        p.ExpiryDate,
                        NetPremiumPayable = p.PremiumLedger != null
                            ? p.PremiumLedger.NetPremiumPayable : 0,
                        // Group member count badge (Feature 5)
                        GroupMemberCount = p.PolicyGroupEmployees.Count()
                    })
                    .ToListAsync();

                var data = rawData.Select(p => new
                {
                    p.PolicyId,
                    p.CoverNoteNumber,
                    p.PolicyNumber,
                    p.ClientName,
                    p.InsurerName,
                    p.PolicyClassName,
                    p.PolicyClassCode,
                    p.PolicyStatus,
                    p.StartDate,
                    p.ExpiryDate,
                    p.NetPremiumPayable,
                    p.GroupMemberCount,
                    DaysToExpiry = (p.ExpiryDate.Date - today).Days,
                    IsGroupPolicy = p.PolicyClassCode == "FWHS"
                        || p.PolicyClassCode == "PA"
                        || p.PolicyClassCode == "MEDHLT"
                });

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading NonMotorPolicies grid");
                return GridError("Failed to load non-motor policies.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: GROUP MEMBERS PAGE (Feature 5)
        // Shows all PolicyGroupEmployees across all group policies
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/GroupMembers
        public IActionResult GroupMembers()
        {
            return View();
        }

        // POST: /Policy/GroupMembersRead
        [HttpPost]
        public async Task<JsonResult> GroupMembersRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null)
        {
            try
            {
                var query = _db.PolicyGroupEmployees
                    .Include(e => e.Policy)
                        .ThenInclude(p => p!.Client)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(e =>
                        e.EmployeeName.Contains(s) ||
                        e.PassportOrNric.Contains(s) ||
                        e.Policy!.CoverNoteNumber.Contains(s) ||
                        e.Policy!.Client!.ClientName.Contains(s));
                }

                var total = await query.CountAsync();

                bool desc = string.Equals(sortDir, "desc",
                    StringComparison.OrdinalIgnoreCase);

                query = sortField switch
                {
                    "EmployeeName" => desc
                        ? query.OrderByDescending(e => e.EmployeeName)
                        : query.OrderBy(e => e.EmployeeName),
                    _ => query.OrderBy(e => e.Policy!.CoverNoteNumber)
                        .ThenBy(e => e.EmployeeName)
                };

                var data = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(e => new
                    {
                        e.EmployeeLinkId,
                        e.PolicyId,
                        CoverNoteNumber = e.Policy!.CoverNoteNumber,
                        ClientName = e.Policy!.Client!.ClientName,
                        e.EmployeeName,
                        e.PassportOrNric,
                        e.Gender,
                        e.NationalityCode,
                        e.DateOfBirth,
                        e.Occupation,
                        e.AnnualWage,
                        e.IsActive
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading GroupMembers grid");
                return GridError("Failed to load group members.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: NEW / EDIT NON-MOTOR POLICY
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/NewNonMotorPolicy
        public async Task<IActionResult> NewNonMotorPolicy()
        {
            await LoadNonMotorDropdownsAsync();
            return View("NonMotorPolicyForm", new NonMotorPolicyFormViewModel());
        }

        // GET: /Policy/EditNonMotorPolicy/5
        public async Task<IActionResult> EditNonMotorPolicy(int id)
        {
            var policy = await _db.Policies
                .Include(p => p.Client)
                .Include(p => p.PolicyClass)
                .Include(p => p.PremiumLedger)
                .FirstOrDefaultAsync(p => p.PolicyId == id);

            if (policy == null)
            {
                TempData["ErrorMessage"] = "Policy not found.";
                return RedirectToAction("NonMotorPolicies");
            }

            if (policy.PolicyClass?.ClassCategory != "NonMotor")
            {
                TempData["ErrorMessage"] = "This is a motor policy — use Edit Motor Policy instead.";
                return RedirectToAction("MotorPolicies");
            }

            await LoadNonMotorDropdownsAsync();

            var ledger = policy.PremiumLedger;

            var vm = new NonMotorPolicyFormViewModel
            {
                PolicyId = policy.PolicyId,
                CoverNoteNumber = policy.CoverNoteNumber,
                PolicyNumber = policy.PolicyNumber,
                ClientId = policy.ClientId,
                ClientDisplay = $"{policy.Client!.ClientCode} — {policy.Client.ClientName}",
                InsurerId = policy.InsurerId,
                PolicyClassCode = policy.PolicyClassCode,
                PolicyStatus = policy.PolicyStatus,
                StartDate = policy.StartDate,
                ExpiryDate = policy.ExpiryDate,
                SumInsured = policy.SumInsured,
                AgentCode = policy.AgentCode,
                Remarks = policy.Remarks,

                GrossPremium = ledger?.GrossPremium ?? 0,
                AddonSpecialPerils = ledger?.AddonSpecialPerils ?? 0,
                AddonNamedDriver = ledger?.AddonNamedDriver ?? 0,
                AddonTotalLoss = ledger?.AddonTotalLoss ?? 0,
                AgentCommission = ledger?.AgentCommission,

                TotalAddonAmount = ledger?.TotalAddonAmount ?? 0,
                ServiceTaxAmount = ledger?.ServiceTaxAmount ?? 0,
                StampDutyAmount = ledger?.StampDutyAmount ?? 10.00m,
                NetPremiumPayable = ledger?.NetPremiumPayable ?? 0
            };

            return View("NonMotorPolicyForm", vm);
        }

        // POST: /Policy/SaveNonMotorPolicy
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SaveNonMotorPolicy(NonMotorPolicyFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var err = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Validation failed.";
                return Json(new { success = false, message = err });
            }

            if (model.ExpiryDate <= model.StartDate)
                return Json(new { success = false, message = "Expiry Date must be after Start Date." });

            try
            {
                var clientExists = await _db.Clients.AnyAsync(c => c.ClientId == model.ClientId);
                if (!clientExists)
                    return Json(new { success = false, message = "Selected client not found." });

                Policy policy;

                if (model.PolicyId == 0)
                {
                    policy = new Policy
                    {
                        CoverNoteNumber = await GenerateCoverNoteNumberAsync(),
                        PolicyNumber = model.PolicyNumber,
                        ClientId = model.ClientId,
                        VehicleId = null, // non-motor: no vehicle
                        InsurerId = model.InsurerId,
                        PolicyClassCode = model.PolicyClassCode,
                        PolicyStatus = model.PolicyStatus,
                        StartDate = model.StartDate,
                        ExpiryDate = model.ExpiryDate,
                        NcdPercentage = 0, // non-motor: no NCD
                        SumInsured = model.SumInsured,
                        AgentCode = string.IsNullOrWhiteSpace(model.AgentCode)
                            ? null : model.AgentCode,
                        Remarks = model.Remarks,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = User.Identity?.Name
                    };

                    _db.Policies.Add(policy);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    var existing = await _db.Policies
                        .Include(p => p.PremiumLedger)
                        .FirstOrDefaultAsync(p => p.PolicyId == model.PolicyId);

                    if (existing == null)
                        return Json(new { success = false, message = "Policy not found." });

                    existing.PolicyNumber = model.PolicyNumber;
                    existing.ClientId = model.ClientId;
                    existing.InsurerId = model.InsurerId;
                    existing.PolicyClassCode = model.PolicyClassCode;
                    existing.PolicyStatus = model.PolicyStatus;
                    existing.StartDate = model.StartDate;
                    existing.ExpiryDate = model.ExpiryDate;
                    existing.SumInsured = model.SumInsured;
                    existing.AgentCode = string.IsNullOrWhiteSpace(model.AgentCode)
                        ? null : model.AgentCode;
                    existing.Remarks = model.Remarks;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.UpdatedBy = User.Identity?.Name;

                    policy = existing;
                }

                // ---- Premium Ledger ----
                var ledger = await _db.PremiumLedgers.FindAsync(policy.PolicyId);
                if (ledger == null)
                {
                    ledger = new PremiumLedger { PolicyId = policy.PolicyId };
                    _db.PremiumLedgers.Add(ledger);
                }

                ledger.SumInsuredAmount = model.SumInsured;
                ledger.GrossPremium = model.GrossPremium;
                ledger.NcdDiscountAmount = 0; // no NCD for non-motor
                ledger.NetPremium = model.GrossPremium;
                ledger.AddonWindscreen = 0;
                ledger.AddonSpecialPerils = model.AddonSpecialPerils;
                ledger.AddonNamedDriver = model.AddonNamedDriver;
                ledger.AddonTotalLoss = model.AddonTotalLoss;
                ledger.AddonEvCharger = 0;
                ledger.AgentCommission = model.AgentCommission;
                ledger.StampDutyAmount = 10.00m;

                ledger.RecalculateTotals();

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    isNew = model.PolicyId == 0,
                    policyId = policy.PolicyId,
                    message = $"Policy {policy.CoverNoteNumber} saved successfully.",
                    redirectUrl = model.IsGroupPolicy
                        ? Url.Action("EditNonMotorPolicy", new { id = policy.PolicyId })
                        : Url.Action("Details", new { id = policy.PolicyId })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Non-Motor Policy");
                return Json(new { success = false, message = "Failed to save policy. " + ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // SECTION: POLICY RENEWAL CLONE (Feature 4)
        // Pre-fills a new policy form from the expired/expiring source
        // policy — agent only needs to update NCD% and confirm premium.
        // ════════════════════════════════════════════════════════════════

        // GET: /Policy/RenewPolicy/5
        public async Task<IActionResult> RenewPolicy(int id)
        {
            var source = await _db.Policies
                .Include(p => p.Client)
                .Include(p => p.Vehicle)
                .Include(p => p.PolicyClass)
                .Include(p => p.PremiumLedger)
                .FirstOrDefaultAsync(p => p.PolicyId == id);

            if (source == null)
            {
                TempData["ErrorMessage"] = "Policy not found.";
                return RedirectToAction("MotorPolicies");
            }

            // Check: prevent renewing a policy that already has a
            // pending/active renewal child
            var existingRenewal = await _db.Policies.AnyAsync(p =>
                p.PreviousPolicyId == id
                && (p.PolicyStatus == "Active"
                    || p.PolicyStatus == "Quoted"
                    || p.PolicyStatus == "Draft"));

            if (existingRenewal)
            {
                TempData["ErrorMessage"] =
                    "A renewal policy already exists for this cover note. " +
                    "Please check the policy list.";

                return source.PolicyClass?.ClassCategory == "Motor"
                    ? RedirectToAction("MotorPolicies")
                    : RedirectToAction("NonMotorPolicies");
            }

            // New policy start = old expiry + 1 day
            var newStart = source.ExpiryDate.AddDays(1);
            var newExpiry = newStart.AddYears(1).AddDays(-1);

            // Step up NCD if the source had no claims (up to max 55%)
            var ncdRates = await _db.NcdRates
                .OrderBy(n => n.NcdPercentage)
                .Select(n => n.NcdPercentage)
                .Distinct()
                .ToListAsync();

            var nextNcd = StepUpNcd(source.NcdPercentage, ncdRates);

            bool isMotor = source.PolicyClass?.ClassCategory == "Motor";

            if (isMotor)
            {
                await LoadMotorPolicyDropdownsAsync();

                var vm = new MotorPolicyFormViewModel
                {
                    // New policy — no PolicyId yet
                    PolicyId = 0,
                    CoverNoteNumber = "(auto-generated on save)",

                    // Copy from source
                    ClientId = source.ClientId,
                    ClientDisplay = source.Client != null
                        ? $"{source.Client.ClientCode} — {source.Client.ClientName}"
                        : null,
                    VehicleId = source.VehicleId ?? 0,
                    VehicleDisplay = source.Vehicle != null
                        ? $"{source.Vehicle.RegistrationNumber} — {source.Vehicle.MakeAndModel}"
                        : null,
                    InsurerId = source.InsurerId,
                    PolicyClassCode = source.PolicyClassCode,
                    PolicyStatus = "Quoted",
                    AgentCode = source.AgentCode,

                    // Updated for new year
                    StartDate = newStart,
                    ExpiryDate = newExpiry,
                    NcdPercentage = nextNcd,
                    SumInsured = source.SumInsured,
                    IsElectricVehicle = source.Vehicle?.IsElectricVehicle ?? false,

                    // Copy last year's premium as starting point
                    GrossPremium = source.PremiumLedger?.GrossPremium ?? 0,
                    AddonWindscreen = source.PremiumLedger?.AddonWindscreen ?? 0,
                    AddonSpecialPerils = source.PremiumLedger?.AddonSpecialPerils ?? 0,
                    AddonNamedDriver = source.PremiumLedger?.AddonNamedDriver ?? 0,
                    AddonTotalLoss = source.PremiumLedger?.AddonTotalLoss ?? 0,
                    AddonEvCharger = source.PremiumLedger?.AddonEvCharger ?? 0,
                    AgentCommission = source.PremiumLedger?.AgentCommission,
                    StampDutyAmount = 10.00m,

                    Remarks = $"Renewal of {source.CoverNoteNumber}"
                };

                // Pass the source policy ID so the view can wire up
                // PreviousPolicyId on save
                ViewBag.PreviousPolicyId = id;
                ViewBag.SourceCoverNote = source.CoverNoteNumber;

                return View("MotorPolicyForm", vm);
            }
            else
            {
                await LoadNonMotorDropdownsAsync();

                var vm = new NonMotorPolicyFormViewModel
                {
                    PolicyId = 0,
                    CoverNoteNumber = "(auto-generated on save)",
                    ClientId = source.ClientId,
                    ClientDisplay = source.Client != null
                        ? $"{source.Client.ClientCode} — {source.Client.ClientName}"
                        : null,
                    InsurerId = source.InsurerId,
                    PolicyClassCode = source.PolicyClassCode,
                    PolicyStatus = "Quoted",
                    AgentCode = source.AgentCode,
                    StartDate = newStart,
                    ExpiryDate = newExpiry,
                    SumInsured = source.SumInsured,
                    GrossPremium = source.PremiumLedger?.GrossPremium ?? 0,
                    AddonSpecialPerils = source.PremiumLedger?.AddonSpecialPerils ?? 0,
                    AddonNamedDriver = source.PremiumLedger?.AddonNamedDriver ?? 0,
                    AddonTotalLoss = source.PremiumLedger?.AddonTotalLoss ?? 0,
                    AgentCommission = source.PremiumLedger?.AgentCommission,
                    Remarks = $"Renewal of {source.CoverNoteNumber}"
                };

                ViewBag.PreviousPolicyId = id;
                ViewBag.SourceCoverNote = source.CoverNoteNumber;

                return View("NonMotorPolicyForm", vm);
            }
        }


        // ================================================================
        // HELPER: Step up NCD to the next tier (capped at 55%)
        // 0 → 25 → 30 → 38.33 → 45 → 55 → stays at 55
        // ================================================================
        private static decimal StepUpNcd(decimal currentNcd, List<decimal> ncdRates)
        {
            if (!ncdRates.Any()) return currentNcd;

            var sorted = ncdRates.OrderBy(n => n).ToList();

            // Find the first rate HIGHER than current
            var next = sorted.FirstOrDefault(n => n > currentNcd);

            // If none found (already at 55%), stay at 55%
            return next > 0 ? next : sorted.Last();
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

            if (model.ExpiryDate <= model.StartDate)
            {
                return Json(new
                {
                    success = false,
                    message = "Expiry Date must be after Start Date."
                });
            }

            try
            {
                var clientExists = await _db.Clients
                    .AnyAsync(c => c.ClientId == model.ClientId);
                if (!clientExists)
                    return Json(new { success = false, message = "Selected client not found." });

                var vehicleExists = await _db.Vehicles
                    .AnyAsync(v => v.VehicleId == model.VehicleId);
                if (!vehicleExists)
                    return Json(new { success = false, message = "Selected vehicle not found." });

                var insurerExists = await _db.Insurers
                    .AnyAsync(i => i.InsurerId == model.InsurerId);
                if (!insurerExists)
                    return Json(new { success = false, message = "Selected insurer not found." });

                Policy policy;

                if (model.PolicyId == 0)
                {
                    // ================================================================
                    // CREATE NEW POLICY
                    // ================================================================
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
                        AgentCode = string.IsNullOrWhiteSpace(model.AgentCode)
                                          ? null : model.AgentCode,
                        Remarks = model.Remarks,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = User.Identity?.Name
                    };

                    // ================================================================
                    // RENEWAL CLONE WIRING (Feature 4)
                    // When this save comes from RenewPolicy(), the form includes a
                    // hidden input: <input name="PreviousPolicyId" value="N" />
                    // We read it here from Request.Form and link the two policies.
                    // ================================================================
                    if (Request.Form.TryGetValue("PreviousPolicyId", out var prevIdStr)
                        && int.TryParse(prevIdStr, out var prevPolicyId)
                        && prevPolicyId > 0)
                    {
                        policy.PreviousPolicyId = prevPolicyId;
                        policy.IsRenewal = true;

                        // Mark the source (prior year) policy so the ExpiredMotor
                        // grid shows "Quote Prepared" badge on that row
                        var sourcePolicy = await _db.Policies.FindAsync(prevPolicyId);
                        if (sourcePolicy != null)
                        {
                            sourcePolicy.RenewalReminderCount += 1;
                        }
                    }

                    _db.Policies.Add(policy);
                    await _db.SaveChangesAsync(); // generates PolicyId for the PremiumLedger FK
                }
                else
                {
                    // ================================================================
                    // UPDATE EXISTING POLICY
                    // ================================================================
                    var existing = await _db.Policies
                        .Include(p => p.PremiumLedger)
                        .FirstOrDefaultAsync(p => p.PolicyId == model.PolicyId);

                    if (existing == null)
                        return Json(new { success = false, message = "Policy not found." });

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
                    existing.AgentCode = string.IsNullOrWhiteSpace(model.AgentCode)
                                              ? null : model.AgentCode;
                    existing.Remarks = model.Remarks;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.UpdatedBy = User.Identity?.Name;

                    policy = existing;
                }

                // ================================================================
                // PREMIUM LEDGER — create or update, then recalculate server-side.
                // Server is the source of truth — never trust the browser's totals.
                //
                // Formula (matches PremiumLedger.RecalculateTotals()):
                //   NcdDiscount      = GrossPremium × NcdPercentage / 100
                //   NetPremium       = GrossPremium - NcdDiscount
                //   TotalAddon       = Windscreen + SpecialPerils + NamedDriver
                //                      + TotalLoss + EvCharger
                //   ServiceTax (8%) = (NetPremium + TotalAddon) × 0.08
                //   StampDuty       = RM 10.00 (fixed — Malaysian law)
                //   NetPremiumPayable = NetPremium + TotalAddon + ServiceTax + StampDuty
                // ================================================================
                var ledger = await _db.PremiumLedgers.FindAsync(policy.PolicyId);
                if (ledger == null)
                {
                    ledger = new PremiumLedger { PolicyId = policy.PolicyId };
                    _db.PremiumLedgers.Add(ledger);
                }

                ledger.SumInsuredAmount = model.SumInsured;
                ledger.GrossPremium = model.GrossPremium;
                ledger.NcdDiscountAmount = Math.Round(
                    model.GrossPremium * model.NcdPercentage / 100m, 2);
                ledger.NetPremium = ledger.GrossPremium - ledger.NcdDiscountAmount;
                ledger.AddonWindscreen = model.AddonWindscreen;
                ledger.AddonSpecialPerils = model.AddonSpecialPerils;
                ledger.AddonNamedDriver = model.AddonNamedDriver;
                ledger.AddonTotalLoss = model.AddonTotalLoss;
                ledger.AddonEvCharger = model.AddonEvCharger;
                ledger.AgentCommission = model.AgentCommission;
                ledger.StampDutyAmount = 10.00m; // fixed by Malaysian law

                // RecalculateTotals() sets TotalAddonAmount, ServiceTaxAmount,
                // and NetPremiumPayable using the same formula as the JS calculator
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
                return Json(new
                {
                    success = false,
                    message = "Failed to save policy. " + ex.Message
                });
            }
        }

        // ================================================================
        // POST: /Policy/SaveMotorPolicy — update to handle PreviousPolicyId
        // ================================================================
        // ⚠️ NOTE: The SaveMotorPolicy action already exists. We only need
        // to add PreviousPolicyId handling inside the "CREATE" branch.
        // Find this block in your existing SaveMotorPolicy:
        //
        //     var policy = new Policy { ... };
        //
        // And add these two lines immediately after:
        //
        //     if (Request.Form.TryGetValue("PreviousPolicyId", out var prevId)
        //         && int.TryParse(prevId, out var prevPolicyId) && prevPolicyId > 0)
        //     {
        //         policy.PreviousPolicyId = prevPolicyId;
        //         policy.IsRenewal = true;
        //     }
        //
        // Same applies to SaveNonMotorPolicy.
        // The hidden input is added to the forms below.

        // ════════════════════════════════════════════════════════════════
        // SECTION: POLICY GROUP EMPLOYEES CRUD (Feature 5)
        // Sub-grid on EditNonMotorPolicy for FWHS / PA / MEDHLT
        // ════════════════════════════════════════════════════════════════

        // POST: /Policy/GroupEmployeesRead
        [HttpPost]
        public async Task<JsonResult> GroupEmployeesRead(int policyId)
        {
            try
            {
                var data = await _db.PolicyGroupEmployees
                    .Where(e => e.PolicyId == policyId)
                    .OrderBy(e => e.EmployeeName)
                    .Select(e => new
                    {
                        e.EmployeeLinkId,
                        e.PolicyId,
                        e.EmployeeName,
                        e.PassportOrNric,
                        e.Gender,
                        e.NationalityCode,
                        e.DateOfBirth,
                        e.Occupation,
                        e.AnnualWage,
                        e.IsActive
                    })
                    .ToListAsync();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading GroupEmployees");
                return GridError("Failed to load group employees.");
            }
        }

        // POST: /Policy/GroupEmployeesSave
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GroupEmployeesSave(PolicyGroupEmployee model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.EmployeeName) ||
                    string.IsNullOrWhiteSpace(model.PassportOrNric))
                {
                    return GridError("Employee Name and Passport/NRIC are required.");
                }

                if (model.EmployeeLinkId == 0)
                {
                    model.IsActive = true;
                    _db.PolicyGroupEmployees.Add(model);
                }
                else
                {
                    var existing = await _db.PolicyGroupEmployees
                        .FindAsync(model.EmployeeLinkId);
                    if (existing == null) return GridError("Employee record not found.");

                    existing.EmployeeName = model.EmployeeName;
                    existing.PassportOrNric = model.PassportOrNric;
                    existing.Gender = model.Gender;
                    existing.NationalityCode = model.NationalityCode;
                    existing.DateOfBirth = model.DateOfBirth;
                    existing.Occupation = model.Occupation;
                    existing.AnnualWage = model.AnnualWage;
                    existing.IsActive = model.IsActive;
                }

                await _db.SaveChangesAsync();
                return GridResult(new[] { model }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving GroupEmployee");
                return GridError("Failed to save employee. " + ex.Message);
            }
        }

        // POST: /Policy/GroupEmployeesDelete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GroupEmployeesDelete(int EmployeeLinkId)
        {
            try
            {
                var existing = await _db.PolicyGroupEmployees.FindAsync(EmployeeLinkId);
                if (existing == null) return GridError("Employee record not found.");

                _db.PolicyGroupEmployees.Remove(existing);
                await _db.SaveChangesAsync();
                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting GroupEmployee");
                return GridError("Failed to delete employee. " + ex.Message);
            }
        }

        // POST: /Policy/ExportGroupRoster — Excel export (Feature 5)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportGroupRoster(int policyId)
        {
            var policy = await _db.Policies
                .Include(p => p.Client)
                .Include(p => p.PolicyClass)
                .Include(p => p.PolicyGroupEmployees)
                .FirstOrDefaultAsync(p => p.PolicyId == policyId);

            if (policy == null) return NotFound();

            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("Group Roster");

            // ---- Header ----
            ws.Cell(1, 1).Value = "IMS Agency — Group Policy Roster";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Merge();

            ws.Cell(2, 1).Value = $"Cover Note: {policy.CoverNoteNumber}";
            ws.Cell(3, 1).Value = $"Client: {policy.Client?.ClientName}";
            ws.Cell(4, 1).Value = $"Policy Class: {policy.PolicyClass?.ClassName}";
            ws.Cell(5, 1).Value = $"Exported: {DateTime.Now:dd/MM/yyyy HH:mm}";

            // ---- Column headers ----
            var headers = new[]
            {
                "No.", "Employee Name", "Passport / NRIC",
                "Gender", "Nationality", "Date of Birth",
                "Occupation", "Annual Wage (RM)"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(7, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor =
                    ClosedXML.Excel.XLColor.FromHtml("#0d6fa4");
                cell.Style.Font.FontColor =
                    ClosedXML.Excel.XLColor.White;
            }

            // ---- Data rows ----
            var employees = policy.PolicyGroupEmployees
                .OrderBy(e => e.EmployeeName)
                .ToList();

            for (int r = 0; r < employees.Count; r++)
            {
                var e = employees[r];
                int row = r + 8;
                ws.Cell(row, 1).Value = r + 1;
                ws.Cell(row, 2).Value = e.EmployeeName;
                ws.Cell(row, 3).Value = e.PassportOrNric;
                ws.Cell(row, 4).Value = e.Gender == "M" ? "Male" : "Female";
                ws.Cell(row, 5).Value = e.NationalityCode ?? "—";
                ws.Cell(row, 6).Value = e.DateOfBirth.HasValue
                    ? e.DateOfBirth.Value.ToString("dd/MM/yyyy") : "—";
                ws.Cell(row, 7).Value = e.Occupation ?? "—";
                ws.Cell(row, 8).Value = e.AnnualWage.HasValue
                     ? e.AnnualWage.Value
                     : "—";

            }

            // ---- Footer total ----
            int lastRow = employees.Count + 8;
            ws.Cell(lastRow, 7).Value = "Total Workers:";
            ws.Cell(lastRow, 7).Style.Font.Bold = true;
            ws.Cell(lastRow, 8).Value = employees.Count;
            ws.Cell(lastRow, 8).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Seek(0, SeekOrigin.Begin);

            var fileName = $"GroupRoster_{policy.CoverNoteNumber}_{DateTime.Now:yyyyMMdd}.xlsx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
    }
}