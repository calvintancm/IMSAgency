using ImsAgency.Web.Data;
using ImsAgency.Web.Models.IMS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize(Roles = "Admin")] // Master Data is Admin-only (per sidebar gating)
    public class MasterController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<MasterController> _logger;

        public MasterController(ApplicationDbContext db, ILogger<MasterController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ================================================================
        // SHARED JSON HELPERS — used by every controller in the project.
        // PropertyNamingPolicy = null keeps PascalCase property names in
        // the JSON response (Kendo 2019's schema.model.fields expects
        // PascalCase to match the C# model exactly).
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


        // ════════════════════════════════════════════════════════════════
        // SECTION: INSURERS
        // ════════════════════════════════════════════════════════════════

        // GET: /Master/Insurers
        public IActionResult Insurers()
        {
            return View();
        }

        // POST: /Master/InsurersRead
        // Kendo grid data source "read" transport — returns ALL insurers
        // (no server paging needed; master data lists are small)
        [HttpPost]
        public async Task<JsonResult> InsurersRead()
        {
            try
            {
                var data = await _db.Insurers
                    .OrderBy(i => i.InsurerCode)
                    .Select(i => new
                    {
                        i.InsurerId,
                        i.InsurerCode,
                        i.InsurerName,
                        i.InsurerType,
                        i.ContactPhone,
                        i.ContactEmail,
                        i.IsActive
                    })
                    .ToListAsync();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Insurers grid");
                return GridError("Failed to load insurers.");
            }
        }

        // POST: /Master/InsurersSave
        // Handles BOTH create (InsurerId == 0) and update (InsurerId > 0)
        // — this is the Kendo inline-edit "save" transport.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> InsurersSave(Insurer model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.InsurerCode) || string.IsNullOrWhiteSpace(model.InsurerName))
                {
                    return GridError("Insurer Code and Insurer Name are required.");
                }

                // Check for duplicate InsurerCode (unique index will also catch
                // this, but checking here gives a friendlier error message)
                var duplicateExists = await _db.Insurers.AnyAsync(i =>
                    i.InsurerCode == model.InsurerCode && i.InsurerId != model.InsurerId);

                if (duplicateExists)
                {
                    return GridError($"Insurer Code '{model.InsurerCode}' already exists.");
                }

                if (model.InsurerId == 0)
                {
                    // ---- CREATE ----
                    model.CreatedAt = DateTime.UtcNow;
                    _db.Insurers.Add(model);
                }
                else
                {
                    // ---- UPDATE ----
                    var existing = await _db.Insurers.FindAsync(model.InsurerId);
                    if (existing == null)
                    {
                        return GridError("Insurer not found.");
                    }

                    existing.InsurerCode = model.InsurerCode;
                    existing.InsurerName = model.InsurerName;
                    existing.InsurerType = model.InsurerType;
                    existing.ContactPhone = model.ContactPhone;
                    existing.ContactEmail = model.ContactEmail;
                    existing.IsActive = model.IsActive;
                }

                await _db.SaveChangesAsync();

                return GridResult(new[] { model }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Insurer");
                return GridError("Failed to save insurer. " + ex.Message);
            }
        }

        // POST: /Master/InsurersDelete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> InsurersDelete(int InsurerId)
        {
            try
            {
                var existing = await _db.Insurers.FindAsync(InsurerId);
                if (existing == null)
                {
                    return GridError("Insurer not found.");
                }

                // Prevent deleting an insurer that has policies attached —
                // suggest deactivating instead (IsActive = false)
                var hasPolicies = await _db.Policies.AnyAsync(p => p.InsurerId == InsurerId);
                if (hasPolicies)
                {
                    return GridError("Cannot delete — this insurer has existing policies. Set 'Active' to No instead.");
                }

                _db.Insurers.Remove(existing);
                await _db.SaveChangesAsync();

                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Insurer");
                return GridError("Failed to delete insurer. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: POLICY CLASSES
        // ════════════════════════════════════════════════════════════════

        // GET: /Master/PolicyClasses
        public IActionResult PolicyClasses()
        {
            return View();
        }

        [HttpPost]
        public async Task<JsonResult> PolicyClassesRead()
        {
            try
            {
                var data = await _db.PolicyClasses
                    .OrderBy(pc => pc.DisplayOrder)
                    .ThenBy(pc => pc.ClassCode)
                    .Select(pc => new
                    {
                        pc.PolicyClassId,
                        pc.ClassCode,
                        pc.ClassName,
                        pc.ClassCategory,
                        pc.DisplayOrder,
                        pc.IsActive
                    })
                    .ToListAsync();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading PolicyClasses grid");
                return GridError("Failed to load policy classes.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> PolicyClassesSave(PolicyClass model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.ClassCode) || string.IsNullOrWhiteSpace(model.ClassName))
                {
                    return GridError("Class Code and Class Name are required.");
                }

                if (model.ClassCategory != "Motor" && model.ClassCategory != "NonMotor")
                {
                    return GridError("Class Category must be 'Motor' or 'NonMotor'.");
                }

                var duplicateExists = await _db.PolicyClasses.AnyAsync(pc =>
                    pc.ClassCode == model.ClassCode && pc.PolicyClassId != model.PolicyClassId);

                if (duplicateExists)
                {
                    return GridError($"Class Code '{model.ClassCode}' already exists.");
                }

                if (model.PolicyClassId == 0)
                {
                    model.CreatedAt = DateTime.UtcNow;
                    _db.PolicyClasses.Add(model);
                }
                else
                {
                    var existing = await _db.PolicyClasses.FindAsync(model.PolicyClassId);
                    if (existing == null)
                    {
                        return GridError("Policy class not found.");
                    }

                    existing.ClassCode = model.ClassCode;
                    existing.ClassName = model.ClassName;
                    existing.ClassCategory = model.ClassCategory;
                    existing.DisplayOrder = model.DisplayOrder;
                    existing.IsActive = model.IsActive;
                }

                await _db.SaveChangesAsync();

                return GridResult(new[] { model }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving PolicyClass");
                return GridError("Failed to save policy class. " + ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> PolicyClassesDelete(int PolicyClassId)
        {
            try
            {
                var existing = await _db.PolicyClasses.FindAsync(PolicyClassId);
                if (existing == null)
                {
                    return GridError("Policy class not found.");
                }

                // PolicyClassCode is an alternate key referenced by
                // Policy.PolicyClassCode — block delete if any policy uses it
                var hasPolicies = await _db.Policies.AnyAsync(p => p.PolicyClassCode == existing.ClassCode);
                if (hasPolicies)
                {
                    return GridError("Cannot delete — this policy class has existing policies. Set 'Active' to No instead.");
                }

                _db.PolicyClasses.Remove(existing);
                await _db.SaveChangesAsync();

                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting PolicyClass");
                return GridError("Failed to delete policy class. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: NCD RATES
        // ════════════════════════════════════════════════════════════════

        // GET: /Master/NcdRates
        public IActionResult NcdRates()
        {
            return View();
        }

        [HttpPost]
        public async Task<JsonResult> NcdRatesRead()
        {
            try
            {
                var data = await _db.NcdRates
                    .OrderByDescending(n => n.EffectiveYear)
                    .ThenBy(n => n.ClaimFreeYears)
                    .Select(n => new
                    {
                        n.NcdRateId,
                        n.ClaimFreeYears,
                        n.NcdPercentage,
                        n.EffectiveYear
                    })
                    .ToListAsync();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading NcdRates grid");
                return GridError("Failed to load NCD rates.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> NcdRatesSave(NcdRate model)
        {
            try
            {
                if (model.ClaimFreeYears < 1 || model.ClaimFreeYears > 5)
                {
                    return GridError("Claim Free Years must be between 1 and 5.");
                }

                if (model.NcdPercentage < 0 || model.NcdPercentage > 100)
                {
                    return GridError("NCD Percentage must be between 0 and 100.");
                }

                if (model.EffectiveYear < 2000 || model.EffectiveYear > 2100)
                {
                    return GridError("Effective Year looks invalid.");
                }

                // Unique index on (ClaimFreeYears, EffectiveYear)
                var duplicateExists = await _db.NcdRates.AnyAsync(n =>
                    n.ClaimFreeYears == model.ClaimFreeYears
                    && n.EffectiveYear == model.EffectiveYear
                    && n.NcdRateId != model.NcdRateId);

                if (duplicateExists)
                {
                    return GridError($"A rate for {model.ClaimFreeYears} claim-free year(s) in {model.EffectiveYear} already exists.");
                }

                if (model.NcdRateId == 0)
                {
                    model.CreatedAt = DateTime.UtcNow;
                    _db.NcdRates.Add(model);
                }
                else
                {
                    var existing = await _db.NcdRates.FindAsync(model.NcdRateId);
                    if (existing == null)
                    {
                        return GridError("NCD rate not found.");
                    }

                    existing.ClaimFreeYears = model.ClaimFreeYears;
                    existing.NcdPercentage = model.NcdPercentage;
                    existing.EffectiveYear = model.EffectiveYear;
                }

                await _db.SaveChangesAsync();

                return GridResult(new[] { model }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving NcdRate");
                return GridError("Failed to save NCD rate. " + ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> NcdRatesDelete(int NcdRateId)
        {
            try
            {
                var existing = await _db.NcdRates.FindAsync(NcdRateId);
                if (existing == null)
                {
                    return GridError("NCD rate not found.");
                }

                // No FK references NcdRates — it's a pure lookup/reference
                // table used by the premium calculator dropdown, so delete
                // is always safe.
                _db.NcdRates.Remove(existing);
                await _db.SaveChangesAsync();

                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting NcdRate");
                return GridError("Failed to delete NCD rate. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: VEHICLE MAKES
        // ════════════════════════════════════════════════════════════════

        // GET: /Master/VehicleMakes
        public IActionResult VehicleMakes()
        {
            return View();
        }

        [HttpPost]
        public async Task<JsonResult> VehicleMakesRead()
        {
            try
            {
                var data = await _db.VehicleMakes
                    .OrderBy(vm => vm.MakeName)
                    .Select(vm => new
                    {
                        vm.VehicleMakeId,
                        vm.MakeCode,
                        vm.MakeName,
                        vm.CountryOfOrigin,
                        vm.IsActive
                    })
                    .ToListAsync();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading VehicleMakes grid");
                return GridError("Failed to load vehicle makes.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> VehicleMakesSave(VehicleMake model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.MakeCode) || string.IsNullOrWhiteSpace(model.MakeName))
                {
                    return GridError("Make Code and Make Name are required.");
                }

                var duplicateExists = await _db.VehicleMakes.AnyAsync(vm =>
                    vm.MakeCode == model.MakeCode && vm.VehicleMakeId != model.VehicleMakeId);

                if (duplicateExists)
                {
                    return GridError($"Make Code '{model.MakeCode}' already exists.");
                }

                if (model.VehicleMakeId == 0)
                {
                    model.CreatedAt = DateTime.UtcNow;
                    _db.VehicleMakes.Add(model);
                }
                else
                {
                    var existing = await _db.VehicleMakes.FindAsync(model.VehicleMakeId);
                    if (existing == null)
                    {
                        return GridError("Vehicle make not found.");
                    }

                    existing.MakeCode = model.MakeCode;
                    existing.MakeName = model.MakeName;
                    existing.CountryOfOrigin = model.CountryOfOrigin;
                    existing.IsActive = model.IsActive;
                }

                await _db.SaveChangesAsync();

                return GridResult(new[] { model }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving VehicleMake");
                return GridError("Failed to save vehicle make. " + ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> VehicleMakesDelete(int VehicleMakeId)
        {
            try
            {
                var existing = await _db.VehicleMakes.FindAsync(VehicleMakeId);
                if (existing == null)
                {
                    return GridError("Vehicle make not found.");
                }

                // VehicleMake is purely a dropdown source for the Vehicle
                // form's "Make" field (free-text MakeAndModel combines make
                // + model on save) — no FK references it, delete is safe.
                _db.VehicleMakes.Remove(existing);
                await _db.SaveChangesAsync();

                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting VehicleMake");
                return GridError("Failed to delete vehicle make. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: MALAYSIAN STATES (static lookup — read-only)
        // ════════════════════════════════════════════════════════════════

        // GET: /Master/MalaysianStates
        public IActionResult MalaysianStates()
        {
            return View();
        }

        [HttpPost]
        public JsonResult MalaysianStatesRead()
        {
            try
            {
                var data = MalaysianStateList.States
                    .Select((name, index) => new { Id = index + 1, StateName = name })
                    .ToList();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading MalaysianStates grid");
                return GridError("Failed to load Malaysian states.");
            }
        }


    }
}