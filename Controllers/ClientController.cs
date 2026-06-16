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
    public class ClientController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ClientController> _logger;

        public ClientController(ApplicationDbContext db, ILogger<ClientController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ================================================================
        // SHARED JSON HELPERS (same as MasterController)
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
        // HELPER: Generate next ClientCode "CLT-2026-0009"
        // ================================================================
        private async Task<string> GenerateClientCodeAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"CLT-{year}-";

            var countThisYear = await _db.Clients
                .CountAsync(c => c.ClientCode.StartsWith(prefix));

            return $"{prefix}{(countThisYear + 1):D4}";
        }


        // ================================================================
        // HELPER: Apply sort to Client query (Kendo serverSorting)
        // ================================================================
        private IQueryable<Client> ApplyClientSort(IQueryable<Client> query, string? sortField, string? sortDir)
        {
            bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

            return sortField switch
            {
                "ClientCode" => desc ? query.OrderByDescending(c => c.ClientCode) : query.OrderBy(c => c.ClientCode),
                "ClientName" => desc ? query.OrderByDescending(c => c.ClientName) : query.OrderBy(c => c.ClientName),
                "ClientType" => desc ? query.OrderByDescending(c => c.ClientType) : query.OrderBy(c => c.ClientType),
                "CreatedAt" => desc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
                _ => query.OrderByDescending(c => c.ClientId) // default: newest first
            };
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: ALL CLIENTS (with search/filter + server paging)
        // ════════════════════════════════════════════════════════════════

        // GET: /Client/AllClients
        public async Task<IActionResult> AllClients()
        {
            var vm = new ClientListViewModel
            {
                TotalClients = await _db.Clients.CountAsync(c => !c.IsBlacklisted),
                IndividualCount = await _db.Clients.CountAsync(c => !c.IsBlacklisted && c.ClientType == "Individual"),
                CompanyCount = await _db.Clients.CountAsync(c => !c.IsBlacklisted && c.ClientType == "Company"),
                BlacklistedCount = await _db.Clients.CountAsync(c => c.IsBlacklisted)
            };

            return View(vm);
        }

        // POST: /Client/ClientsRead
        // Server-side filtering + paging. Excludes blacklisted clients
        // (those only show on the Blacklisted page).
        [HttpPost]
        public async Task<JsonResult> ClientsRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null, string? clientType = null, string? status = null)
        {
            try
            {
                var query = _db.Clients.AsQueryable().Where(c => !c.IsBlacklisted);

                // ---- Free-text search across Code / Name / ID Number ----
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(c =>
                        c.ClientCode.Contains(s) ||
                        c.ClientName.Contains(s) ||
                        c.IdentificationNumber.Contains(s));
                }

                // ---- Client Type filter ----
                if (!string.IsNullOrWhiteSpace(clientType) && clientType != "All")
                {
                    query = query.Where(c => c.ClientType == clientType);
                }

                // ---- Active/Inactive filter ----
                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                {
                    bool wantActive = status == "Active";
                    query = query.Where(c => c.IsActive == wantActive);
                }

                var total = await query.CountAsync();

                query = ApplyClientSort(query, sortField, sortDir);

                var data = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(c => new
                    {
                        c.ClientId,
                        c.ClientCode,
                        c.ClientType,
                        c.ClientName,
                        c.IdentificationNumber,
                        c.IdentificationType,
                        c.EmailAddress,
                        c.IsActive,
                        // Primary phone, if any (used in grid + for quick WhatsApp reference)
                        PrimaryPhone = c.ClientPhones
                            .Where(p => p.IsPrimary)
                            .Select(p => p.PhoneNumber)
                            .FirstOrDefault(),
                        PolicyCount = c.Policies.Count()
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Clients grid");
                return GridError("Failed to load clients.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: BLACKLISTED CLIENTS
        // ════════════════════════════════════════════════════════════════

        // GET: /Client/Blacklisted
        [Authorize(Roles = "Admin,SeniorAgent")] // matches sidebar: isSeniorAgent gating
        public IActionResult Blacklisted()
        {
            return View();
        }

        // POST: /Client/BlacklistedRead
        [HttpPost]
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<JsonResult> BlacklistedRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null)
        {
            try
            {
                var query = _db.Clients.AsQueryable().Where(c => c.IsBlacklisted);

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(c =>
                        c.ClientCode.Contains(s) ||
                        c.ClientName.Contains(s) ||
                        c.IdentificationNumber.Contains(s));
                }

                var total = await query.CountAsync();

                query = ApplyClientSort(query, sortField, sortDir);

                var data = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(c => new
                    {
                        c.ClientId,
                        c.ClientCode,
                        c.ClientType,
                        c.ClientName,
                        c.IdentificationNumber,
                        c.Remarks,
                        c.UpdatedAt
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Blacklisted Clients grid");
                return GridError("Failed to load blacklisted clients.");
            }
        }

        // POST: /Client/ToggleBlacklist
        // Used by BOTH AllClients (to blacklist) and Blacklisted (to un-blacklist).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<JsonResult> ToggleBlacklist(int ClientId, string? Reason)
        {
            try
            {
                var client = await _db.Clients.FindAsync(ClientId);
                if (client == null)
                {
                    return GridError("Client not found.");
                }

                client.IsBlacklisted = !client.IsBlacklisted;
                client.UpdatedAt = DateTime.UtcNow;

                if (client.IsBlacklisted)
                {
                    // Append the reason to Remarks rather than overwrite,
                    // so prior remarks aren't lost
                    var note = $"[Blacklisted {DateTime.UtcNow:yyyy-MM-dd}] {Reason}".Trim();
                    client.Remarks = string.IsNullOrWhiteSpace(client.Remarks)
                        ? note
                        : client.Remarks + " | " + note;
                }
                else
                {
                    var note = $"[Un-blacklisted {DateTime.UtcNow:yyyy-MM-dd}] {Reason}".Trim();
                    client.Remarks = string.IsNullOrWhiteSpace(client.Remarks)
                        ? note
                        : client.Remarks + " | " + note;
                }

                await _db.SaveChangesAsync();

                return GridResult(new[] { new { client.ClientId, client.IsBlacklisted } }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling blacklist for Client {ClientId}", ClientId);
                return GridError("Failed to update blacklist status. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: DELETE CLIENT
        // ════════════════════════════════════════════════════════════════

        // POST: /Client/DeleteClient
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<JsonResult> DeleteClient(int ClientId)
        {
            try
            {
                var client = await _db.Clients.FindAsync(ClientId);
                if (client == null)
                {
                    return GridError("Client not found.");
                }

                // Block delete if the client has ANY policies — deleting
                // would either fail (Restrict FK on payments) or cascade-
                // delete policy history, which we never want.
                var hasPolicies = await _db.Policies.AnyAsync(p => p.ClientId == ClientId);
                if (hasPolicies)
                {
                    return GridError("Cannot delete — this client has existing policies. Deactivate the client instead (set Active = No).");
                }

                _db.Clients.Remove(client); // cascades to ClientPhones/ClientAddresses
                await _db.SaveChangesAsync();

                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Client {ClientId}", ClientId);
                return GridError("Failed to delete client. " + ex.Message);
            }
        }




        // ════════════════════════════════════════════════════════════════
        // SECTION: ADD / EDIT CLIENT (shared form)
        // ════════════════════════════════════════════════════════════════

        // GET: /Client/AddClient
        public IActionResult AddClient()
        {
            var vm = new ClientFormViewModel(); // ClientId = 0 -> new client
            return View("ClientForm", vm);
        }

        // GET: /Client/EditClient/5
        public async Task<IActionResult> EditClient(int id)
        {
            var client = await _db.Clients.FindAsync(id);
            if (client == null)
            {
                TempData["ErrorMessage"] = "Client not found.";
                return RedirectToAction("AllClients");
            }

            var vm = new ClientFormViewModel
            {
                ClientId = client.ClientId,
                ClientCode = client.ClientCode,
                ClientType = client.ClientType,
                ClientName = client.ClientName,
                IdentificationNumber = client.IdentificationNumber,
                IdentificationType = client.IdentificationType,
                DateOfBirth = client.DateOfBirth,
                Gender = client.Gender,
                EmailAddress = client.EmailAddress,
                OccupationOrBusiness = client.OccupationOrBusiness,
                IsActive = client.IsActive,
                Remarks = client.Remarks
            };

            return View("ClientForm", vm);
        }

        // POST: /Client/SaveClient
        // AJAX endpoint — returns JSON so the form can stay on-page and
        // activate the Phone/Address sub-grids immediately after a new
        // client is created (no full page redirect needed).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SaveClient(ClientFormViewModel model)
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
                if (model.ClientId == 0)
                {
                    // ---- CREATE ----
                    var client = new Client
                    {
                        ClientCode = await GenerateClientCodeAsync(),
                        ClientType = model.ClientType,
                        ClientName = model.ClientName,
                        IdentificationNumber = model.IdentificationNumber,
                        IdentificationType = model.IdentificationType,
                        DateOfBirth = model.DateOfBirth,
                        Gender = model.Gender,
                        EmailAddress = model.EmailAddress,
                        OccupationOrBusiness = model.OccupationOrBusiness,
                        IsActive = model.IsActive,
                        Remarks = model.Remarks,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.Clients.Add(client);
                    await _db.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        isNew = true,
                        clientId = client.ClientId,
                        clientCode = client.ClientCode,
                        message = $"Client {client.ClientCode} created successfully.",
                        redirectUrl = Url.Action("EditClient", new { id = client.ClientId })
                    });
                }
                else
                {
                    // ---- UPDATE ----
                    var existing = await _db.Clients.FindAsync(model.ClientId);
                    if (existing == null)
                    {
                        return Json(new { success = false, message = "Client not found." });
                    }

                    existing.ClientType = model.ClientType;
                    existing.ClientName = model.ClientName;
                    existing.IdentificationNumber = model.IdentificationNumber;
                    existing.IdentificationType = model.IdentificationType;
                    existing.DateOfBirth = model.DateOfBirth;
                    existing.Gender = model.Gender;
                    existing.EmailAddress = model.EmailAddress;
                    existing.OccupationOrBusiness = model.OccupationOrBusiness;
                    existing.IsActive = model.IsActive;
                    existing.Remarks = model.Remarks;
                    existing.UpdatedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync();

                    return Json(new { success = true, isNew = false, message = "Client details updated successfully." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Client");
                return Json(new { success = false, message = "Failed to save client. " + ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: CLIENT PHONES (sub-grid on EditClient)
        // ════════════════════════════════════════════════════════════════

        [HttpPost]
        public async Task<JsonResult> ClientPhonesRead(int clientId)
        {
            try
            {
                var data = await _db.ClientPhones
                    .Where(p => p.ClientId == clientId)
                    .OrderByDescending(p => p.IsPrimary)
                    .ThenBy(p => p.PhoneId)
                    .Select(p => new
                    {
                        p.PhoneId,
                        p.ClientId,
                        p.PhoneNumber,
                        p.PhoneType,
                        p.IsPrimary,
                        p.IsWhatsApp
                    })
                    .ToListAsync();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading ClientPhones grid");
                return GridError("Failed to load phone numbers.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ClientPhonesSave(ClientPhone model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.PhoneNumber))
                {
                    return GridError("Phone Number is required.");
                }

                if (model.ClientId == 0)
                {
                    return GridError("Client must be saved before adding phone numbers.");
                }

                // If this phone is being set as Primary, unset Primary on
                // all OTHER phones for this client first (one primary rule)
                if (model.IsPrimary)
                {
                    var others = await _db.ClientPhones
                        .Where(p => p.ClientId == model.ClientId && p.PhoneId != model.PhoneId)
                        .ToListAsync();

                    foreach (var o in others) o.IsPrimary = false;
                }

                if (model.PhoneId == 0)
                {
                    _db.ClientPhones.Add(model);
                }
                else
                {
                    var existing = await _db.ClientPhones.FindAsync(model.PhoneId);
                    if (existing == null) return GridError("Phone record not found.");

                    existing.PhoneNumber = model.PhoneNumber;
                    existing.PhoneType = model.PhoneType;
                    existing.IsPrimary = model.IsPrimary;
                    existing.IsWhatsApp = model.IsWhatsApp;
                }

                await _db.SaveChangesAsync();

                return GridResult(new[] { model }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving ClientPhone");
                return GridError("Failed to save phone number. " + ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ClientPhonesDelete(int PhoneId)
        {
            try
            {
                var existing = await _db.ClientPhones.FindAsync(PhoneId);
                if (existing == null) return GridError("Phone record not found.");

                _db.ClientPhones.Remove(existing);
                await _db.SaveChangesAsync();

                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting ClientPhone");
                return GridError("Failed to delete phone number. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: CLIENT ADDRESSES (sub-grid on EditClient)
        // ════════════════════════════════════════════════════════════════

        [HttpPost]
        public async Task<JsonResult> ClientAddressesRead(int clientId)
        {
            try
            {
                var data = await _db.ClientAddresses
                    .Where(a => a.ClientId == clientId)
                    .OrderByDescending(a => a.IsPrimary)
                    .ThenBy(a => a.AddressId)
                    .Select(a => new
                    {
                        a.AddressId,
                        a.ClientId,
                        a.AddressType,
                        a.AddressLine1,
                        a.AddressLine2,
                        a.City,
                        a.State,
                        a.Postcode,
                        a.IsPrimary
                    })
                    .ToListAsync();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading ClientAddresses grid");
                return GridError("Failed to load addresses.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ClientAddressesSave(ClientAddress model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.AddressLine1) || string.IsNullOrWhiteSpace(model.City)
                    || string.IsNullOrWhiteSpace(model.State) || string.IsNullOrWhiteSpace(model.Postcode))
                {
                    return GridError("Address Line 1, City, State, and Postcode are required.");
                }

                if (model.ClientId == 0)
                {
                    return GridError("Client must be saved before adding addresses.");
                }

                if (model.IsPrimary)
                {
                    var others = await _db.ClientAddresses
                        .Where(a => a.ClientId == model.ClientId && a.AddressId != model.AddressId)
                        .ToListAsync();

                    foreach (var o in others) o.IsPrimary = false;
                }

                if (model.AddressId == 0)
                {
                    _db.ClientAddresses.Add(model);
                }
                else
                {
                    var existing = await _db.ClientAddresses.FindAsync(model.AddressId);
                    if (existing == null) return GridError("Address record not found.");

                    existing.AddressType = model.AddressType;
                    existing.AddressLine1 = model.AddressLine1;
                    existing.AddressLine2 = model.AddressLine2;
                    existing.City = model.City;
                    existing.State = model.State;
                    existing.Postcode = model.Postcode;
                    existing.IsPrimary = model.IsPrimary;
                }

                await _db.SaveChangesAsync();

                return GridResult(new[] { model }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving ClientAddress");
                return GridError("Failed to save address. " + ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ClientAddressesDelete(int AddressId)
        {
            try
            {
                var existing = await _db.ClientAddresses.FindAsync(AddressId);
                if (existing == null) return GridError("Address record not found.");

                _db.ClientAddresses.Remove(existing);
                await _db.SaveChangesAsync();

                return GridResult(Array.Empty<object>(), 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting ClientAddress");
                return GridError("Failed to delete address. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: CLIENT 360° VIEW (Feature 8)
        // ════════════════════════════════════════════════════════════════

        // GET: /Client/Profile/5
        public async Task<IActionResult> Profile(int id)
        {
            var client = await _db.Clients.FindAsync(id);
            if (client == null)
            {
                TempData["ErrorMessage"] = "Client not found.";
                return RedirectToAction("AllClients");
            }

            var vm = new ClientProfileViewModel
            {
                ClientId = client.ClientId,
                ClientCode = client.ClientCode,
                ClientType = client.ClientType,
                ClientName = client.ClientName,
                IdentificationNumber = client.IdentificationNumber,
                IdentificationType = client.IdentificationType,
                DateOfBirth = client.DateOfBirth,
                Gender = client.Gender,
                EmailAddress = client.EmailAddress,
                OccupationOrBusiness = client.OccupationOrBusiness,
                IsActive = client.IsActive,
                IsBlacklisted = client.IsBlacklisted,
                Remarks = client.Remarks,
                CreatedAt = client.CreatedAt
            };

            // ---- Phones & Addresses ----
            vm.Phones = await _db.ClientPhones
                .Where(p => p.ClientId == id)
                .OrderByDescending(p => p.IsPrimary)
                .Select(p => new ClientPhoneItem
                {
                    PhoneNumber = p.PhoneNumber,
                    PhoneType = p.PhoneType,
                    IsPrimary = p.IsPrimary,
                    IsWhatsApp = p.IsWhatsApp
                })
                .ToListAsync();

            vm.Addresses = await _db.ClientAddresses
                .Where(a => a.ClientId == id)
                .OrderByDescending(a => a.IsPrimary)
                .Select(a => new ClientAddressItem
                {
                    AddressType = a.AddressType,
                    AddressLine1 = a.AddressLine1,
                    AddressLine2 = a.AddressLine2,
                    City = a.City,
                    State = a.State,
                    Postcode = a.Postcode,
                    IsPrimary = a.IsPrimary
                })
                .ToListAsync();

            // ---- Policy timeline (all policies, active + history) ----
            vm.Policies = await _db.Policies
                .Include(p => p.PolicyClass)
                .Include(p => p.Insurer)
                .Include(p => p.Vehicle)
                .Include(p => p.PremiumLedger)
                .Where(p => p.ClientId == id)
                .OrderByDescending(p => p.StartDate)
                .Select(p => new PolicyTimelineItem
                {
                    PolicyId = p.PolicyId,
                    CoverNoteNumber = p.CoverNoteNumber,
                    RegistrationNumber = p.Vehicle != null ? p.Vehicle.RegistrationNumber : null,
                    PolicyClassName = p.PolicyClass!.ClassName,
                    InsurerName = p.Insurer!.InsurerName,
                    PolicyStatus = p.PolicyStatus,
                    StartDate = p.StartDate,
                    ExpiryDate = p.ExpiryDate,
                    NetPremiumPayable = p.PremiumLedger != null ? p.PremiumLedger.NetPremiumPayable : 0,
                    IsRenewal = p.IsRenewal
                })
                .ToListAsync();

            vm.TotalPolicyCount = vm.Policies.Count;
            vm.ActivePolicyCount = vm.Policies.Count(p => p.PolicyStatus == "Active");

            // ---- Payment history ----
            vm.Payments = await _db.CustomerPaymentLedgers
                .Include(pay => pay.Policy)
                .Where(pay => pay.ClientId == id)
                .OrderByDescending(pay => pay.PaymentDate)
                .Select(pay => new PaymentHistoryItem
                {
                    PaymentDate = pay.PaymentDate,
                    ReceiptNumber = pay.ReceiptNumber,
                    CoverNoteNumber = pay.Policy!.CoverNoteNumber,
                    PaymentMethod = pay.PaymentMethod,
                    AmountPaid = pay.AmountPaid,
                    IsClearedAndSettled = pay.IsClearedAndSettled
                })
                .ToListAsync();

            vm.TotalPremiumCollected = vm.Payments.Sum(p => p.AmountPaid);

            // ---- Outstanding balance across this client's policies ----
            var ledgerTotals = await _db.PremiumLedgers
                .Where(pl => _db.Policies.Any(p => p.PolicyId == pl.PolicyId && p.ClientId == id))
                .Select(pl => new
                {
                    pl.PolicyId,
                    pl.NetPremiumPayable,
                    TotalPaid = _db.CustomerPaymentLedgers
                        .Where(c => c.PolicyId == pl.PolicyId)
                        .Sum(c => (decimal?)c.AmountPaid) ?? 0
                })
                .ToListAsync();

            vm.TotalOutstandingBalance = ledgerTotals
                .Sum(x => Math.Max(0, x.NetPremiumPayable - x.TotalPaid));

            // ---- Claims history ----
            vm.Claims = await _db.Claims
                .Include(c => c.Policy)
                .Where(c => c.Policy!.ClientId == id)
                .OrderByDescending(c => c.ClaimDate)
                .Select(c => new ClaimHistoryItem
                {
                    ClaimReferenceNumber = c.ClaimReferenceNumber,
                    CoverNoteNumber = c.Policy!.CoverNoteNumber,
                    ClaimDate = c.ClaimDate,
                    ClaimType = c.ClaimType,
                    ClaimStatus = c.ClaimStatus,
                    ApprovedClaimAmount = c.ApprovedClaimAmount,
                    EstimatedLossAmount = c.EstimatedLossAmount
                })
                .ToListAsync();

            // ---- Renewal notice log ----
            vm.RenewalNotices = await _db.RenewalNotices
                .Include(rn => rn.Policy)
                .Where(rn => rn.Policy!.ClientId == id)
                .OrderByDescending(rn => rn.SentAt)
                .Select(rn => new RenewalNoticeItem
                {
                    CoverNoteNumber = rn.Policy!.CoverNoteNumber,
                    NoticeType = rn.NoticeType,
                    SentAt = rn.SentAt,
                    Channel = rn.Channel,
                    IsDelivered = rn.IsDelivered
                })
                .ToListAsync();

            return View(vm);
        }

    }
}