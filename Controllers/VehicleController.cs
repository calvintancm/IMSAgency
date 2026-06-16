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
    public class VehicleController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<VehicleController> _logger;

        public VehicleController(ApplicationDbContext db, ILogger<VehicleController> logger)
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
        // HELPER: Sort
        // ================================================================
        private IQueryable<Vehicle> ApplyVehicleSort(IQueryable<Vehicle> query, string? sortField, string? sortDir)
        {
            bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

            return sortField switch
            {
                "RegistrationNumber" => desc ? query.OrderByDescending(v => v.RegistrationNumber) : query.OrderBy(v => v.RegistrationNumber),
                "MakeAndModel" => desc ? query.OrderByDescending(v => v.MakeAndModel) : query.OrderBy(v => v.MakeAndModel),
                "ManufactureYear" => desc ? query.OrderByDescending(v => v.ManufactureYear) : query.OrderBy(v => v.ManufactureYear),
                "CreatedAt" => desc ? query.OrderByDescending(v => v.CreatedAt) : query.OrderBy(v => v.CreatedAt),
                _ => query.OrderByDescending(v => v.VehicleId) // default: newest first
            };
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: ALL VEHICLES (with search/filter + server paging)
        // ════════════════════════════════════════════════════════════════

        // GET: /Vehicle/AllVehicles
        public async Task<IActionResult> AllVehicles()
        {
            var vm = new VehicleListViewModel
            {
                TotalVehicles = await _db.Vehicles.CountAsync(),
                ActiveVehicles = await _db.Vehicles.CountAsync(v => v.IsActive),
                ElectricVehicleCount = await _db.Vehicles.CountAsync(v => v.IsElectricVehicle),
                VehiclesWithActivePolicy = await _db.Vehicles
                    .CountAsync(v => v.Policies.Any(p => p.PolicyStatus == "Active"))
            };

            return View(vm);
        }

        // POST: /Vehicle/VehiclesRead
        [HttpPost]
        public async Task<JsonResult> VehiclesRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null, string? vehicleUsage = null,
            string? isElectric = null, string? status = null)
        {
            try
            {
                var query = _db.Vehicles.AsQueryable();

                // ---- Free-text search across Registration / Make&Model ----
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(v =>
                        v.RegistrationNumber.Contains(s) ||
                        v.MakeAndModel.Contains(s));
                }

                // ---- Vehicle Usage filter ----
                if (!string.IsNullOrWhiteSpace(vehicleUsage) && vehicleUsage != "All")
                {
                    query = query.Where(v => v.VehicleUsage == vehicleUsage);
                }

                // ---- EV filter ----
                if (!string.IsNullOrWhiteSpace(isElectric) && isElectric != "All")
                {
                    bool wantEv = isElectric == "Yes";
                    query = query.Where(v => v.IsElectricVehicle == wantEv);
                }

                // ---- Active/Inactive filter ----
                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                {
                    bool wantActive = status == "Active";
                    query = query.Where(v => v.IsActive == wantActive);
                }

                var total = await query.CountAsync();

                query = ApplyVehicleSort(query, sortField, sortDir);

                var data = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(v => new
                    {
                        v.VehicleId,
                        v.RegistrationNumber,
                        v.MakeAndModel,
                        v.ManufactureYear,
                        v.EngineCapacityCC,
                        v.VehicleUsage,
                        v.IsElectricVehicle,
                        v.VehicleColour,
                        v.IsActive,
                        PolicyCount = v.Policies.Count(),
                        HasActivePolicy = v.Policies.Any(p => p.PolicyStatus == "Active")
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Vehicles grid");
                return GridError("Failed to load vehicles.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: ADD / EDIT VEHICLE (shared form)
        // ════════════════════════════════════════════════════════════════

        // GET: /Vehicle/AddVehicle
        public async Task<IActionResult> AddVehicle()
        {
            await LoadVehicleMakesAsync();
            var vm = new VehicleFormViewModel();
            return View("VehicleForm", vm);
        }

        // GET: /Vehicle/EditVehicle/5
        public async Task<IActionResult> EditVehicle(int id)
        {
            var vehicle = await _db.Vehicles.FindAsync(id);
            if (vehicle == null)
            {
                TempData["ErrorMessage"] = "Vehicle not found.";
                return RedirectToAction("AllVehicles");
            }

            await LoadVehicleMakesAsync();

            var vm = new VehicleFormViewModel
            {
                VehicleId = vehicle.VehicleId,
                RegistrationNumber = vehicle.RegistrationNumber,
                MakeAndModel = vehicle.MakeAndModel,
                ManufactureYear = vehicle.ManufactureYear,
                EngineNumber = vehicle.EngineNumber,
                ChassisNumber = vehicle.ChassisNumber,
                EngineCapacityCC = vehicle.EngineCapacityCC,
                SeatingCapacity = vehicle.SeatingCapacity,
                VehicleUsage = vehicle.VehicleUsage,
                IsElectricVehicle = vehicle.IsElectricVehicle,
                VehicleColour = vehicle.VehicleColour,
                MarketValue = vehicle.MarketValue,
                IsActive = vehicle.IsActive
            };

            return View("VehicleForm", vm);
        }

        // Loads active Vehicle Makes into ViewBag for the "Make" quick-fill dropdown
        private async Task LoadVehicleMakesAsync()
        {
            ViewBag.VehicleMakes = await _db.VehicleMakes
                .Where(m => m.IsActive)
                .OrderBy(m => m.MakeName)
                .Select(m => m.MakeName)
                .ToListAsync();
        }

        // POST: /Vehicle/SaveVehicle
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SaveVehicle(VehicleFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var firstError = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Validation failed.";

                return Json(new { success = false, message = firstError });
            }

            try
            {
                // ---- Registration Number uniqueness check ----
                var regNo = model.RegistrationNumber.Trim().ToUpper();
                var duplicateExists = await _db.Vehicles.AnyAsync(v =>
                    v.RegistrationNumber.ToUpper() == regNo && v.VehicleId != model.VehicleId);

                if (duplicateExists)
                {
                    return Json(new { success = false, message = $"Registration Number '{model.RegistrationNumber}' already exists." });
                }

                if (model.VehicleId == 0)
                {
                    // ---- CREATE ----
                    var vehicle = new Vehicle
                    {
                        RegistrationNumber = model.RegistrationNumber.Trim(),
                        MakeAndModel = model.MakeAndModel.Trim(),
                        ManufactureYear = model.ManufactureYear,
                        EngineNumber = model.EngineNumber,
                        ChassisNumber = model.ChassisNumber,
                        EngineCapacityCC = model.EngineCapacityCC,
                        SeatingCapacity = model.SeatingCapacity,
                        VehicleUsage = model.VehicleUsage,
                        IsElectricVehicle = model.IsElectricVehicle,
                        VehicleColour = model.VehicleColour,
                        MarketValue = model.MarketValue,
                        IsActive = model.IsActive,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.Vehicles.Add(vehicle);
                    await _db.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        isNew = true,
                        vehicleId = vehicle.VehicleId,
                        message = $"Vehicle {vehicle.RegistrationNumber} created successfully.",
                        redirectUrl = Url.Action("AllVehicles")
                    });
                }
                else
                {
                    // ---- UPDATE ----
                    var existing = await _db.Vehicles.FindAsync(model.VehicleId);
                    if (existing == null)
                    {
                        return Json(new { success = false, message = "Vehicle not found." });
                    }

                    existing.RegistrationNumber = model.RegistrationNumber.Trim();
                    existing.MakeAndModel = model.MakeAndModel.Trim();
                    existing.ManufactureYear = model.ManufactureYear;
                    existing.EngineNumber = model.EngineNumber;
                    existing.ChassisNumber = model.ChassisNumber;
                    existing.EngineCapacityCC = model.EngineCapacityCC;
                    existing.SeatingCapacity = model.SeatingCapacity;
                    existing.VehicleUsage = model.VehicleUsage;
                    existing.IsElectricVehicle = model.IsElectricVehicle;
                    existing.VehicleColour = model.VehicleColour;
                    existing.MarketValue = model.MarketValue;
                    existing.IsActive = model.IsActive;

                    await _db.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        isNew = false,
                        message = "Vehicle details updated successfully.",
                        redirectUrl = Url.Action("AllVehicles")
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Vehicle");
                return Json(new { success = false, message = "Failed to save vehicle. " + ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: DELETE / DEACTIVATE VEHICLE
        // ════════════════════════════════════════════════════════════════

        // POST: /Vehicle/DeleteVehicle
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<JsonResult> DeleteVehicle(int VehicleId)
        {
            try
            {
                var vehicle = await _db.Vehicles.FindAsync(VehicleId);
                if (vehicle == null)
                {
                    return GridError("Vehicle not found.");
                }

                var hasPolicies = await _db.Policies.AnyAsync(p => p.VehicleId == VehicleId);
                if (hasPolicies)
                {
                    return GridError("Cannot delete — this vehicle has existing policies. Set 'Active' to No instead.");
                }

                _db.Vehicles.Remove(vehicle);
                await _db.SaveChangesAsync();

                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Vehicle {VehicleId}", VehicleId);
                return GridError("Failed to delete vehicle. " + ex.Message);
            }
        }

        // POST: /Vehicle/ToggleActive — quick deactivate from the grid
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ToggleActive(int VehicleId)
        {
            try
            {
                var vehicle = await _db.Vehicles.FindAsync(VehicleId);
                if (vehicle == null)
                {
                    return GridError("Vehicle not found.");
                }

                vehicle.IsActive = !vehicle.IsActive;
                await _db.SaveChangesAsync();

                return GridResult(new[] { new { vehicle.VehicleId, vehicle.IsActive } }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling active state for Vehicle {VehicleId}", VehicleId);
                return GridError("Failed to update vehicle status. " + ex.Message);
            }
        }
    }
}