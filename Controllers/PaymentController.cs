using ImsAgency.Web.Data;
using ImsAgency.Web.Models.IMS;
using ImsAgency.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize(Roles = "Admin,SeniorAgent,Agent")]
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(ApplicationDbContext db, ILogger<PaymentController> logger)
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
        // HELPER: Generate next ReceiptNumber "RCT-2026-00012"
        // ================================================================
        private async Task<string> GenerateReceiptNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"RCT-{year}-";

            var existingSeqs = await _db.CustomerPaymentLedgers
                .Where(p => p.ReceiptNumber.StartsWith(prefix))
                .Select(p => p.ReceiptNumber.Substring(prefix.Length))
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
        // SECTION: ALL PAYMENTS
        // ════════════════════════════════════════════════════════════════

        // GET: /Payment/AllPayments
        public async Task<IActionResult> AllPayments()
        {
            var vm = new PaymentListViewModel
            {
                TotalPayments = await _db.CustomerPaymentLedgers.CountAsync(),

                TotalAmountCollected = await _db.CustomerPaymentLedgers
                    .SumAsync(p => (decimal?)p.AmountPaid) ?? 0,

                PendingClearance = await _db.CustomerPaymentLedgers
                    .CountAsync(p => !p.IsClearedAndSettled),

                OutstandingBalance = await ComputeOutstandingAsync()
            };

            return View(vm);
        }

        private async Task<decimal> ComputeOutstandingAsync()
        {
            var ledgerTotals = await _db.PremiumLedgers
                .Select(pl => new
                {
                    pl.PolicyId,
                    pl.NetPremiumPayable,
                    TotalPaid = _db.CustomerPaymentLedgers
                        .Where(c => c.PolicyId == pl.PolicyId)
                        .Sum(c => (decimal?)c.AmountPaid) ?? 0
                })
                .ToListAsync();

            return ledgerTotals.Sum(x => Math.Max(0, x.NetPremiumPayable - x.TotalPaid));
        }

        // POST: /Payment/PaymentsRead
        [HttpPost]
        public async Task<JsonResult> PaymentsRead(
            int page = 1, int pageSize = 15,
            string? sortField = null, string? sortDir = null,
            string? searchText = null,
            string? paymentMethod = null,
            string? cleared = null)
        {
            try
            {
                var query = _db.CustomerPaymentLedgers
                    .Include(p => p.Client)
                    .Include(p => p.Policy)
                        .ThenInclude(pol => pol!.Vehicle)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    query = query.Where(p =>
                        p.ReceiptNumber.Contains(s) ||
                        p.Client!.ClientName.Contains(s) ||
                        p.Policy!.CoverNoteNumber.Contains(s) ||
                        (p.ReferenceNumber != null && p.ReferenceNumber.Contains(s)));
                }

                if (!string.IsNullOrWhiteSpace(paymentMethod) && paymentMethod != "All")
                    query = query.Where(p => p.PaymentMethod == paymentMethod);

                if (!string.IsNullOrWhiteSpace(cleared) && cleared != "All")
                {
                    bool wantCleared = cleared == "Yes";
                    query = query.Where(p => p.IsClearedAndSettled == wantCleared);
                }

                var total = await query.CountAsync();

                bool desc = string.Equals(sortDir, "desc",
                    StringComparison.OrdinalIgnoreCase);

                query = sortField switch
                {
                    "PaymentDate" => desc
                        ? query.OrderByDescending(p => p.PaymentDate)
                        : query.OrderBy(p => p.PaymentDate),
                    "AmountPaid" => desc
                        ? query.OrderByDescending(p => p.AmountPaid)
                        : query.OrderBy(p => p.AmountPaid),
                    "ClientName" => desc
                        ? query.OrderByDescending(p => p.Client!.ClientName)
                        : query.OrderBy(p => p.Client!.ClientName),
                    _ => query.OrderByDescending(p => p.PaymentId)
                };

                var data = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.PaymentId,
                        p.ReceiptNumber,
                        ClientName = p.Client!.ClientName,
                        CoverNoteNumber = p.Policy!.CoverNoteNumber,
                        RegistrationNumber = p.Policy!.Vehicle != null
                            ? p.Policy!.Vehicle!.RegistrationNumber : null,
                        p.PaymentDate,
                        p.PaymentMethod,
                        p.AmountPaid,
                        p.BankOrIssueName,
                        p.ReferenceNumber,
                        p.IsClearedAndSettled,
                        p.RecordedBy
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Payments grid");
                return GridError("Failed to load payments.");
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: RECORD PAYMENT
        // ════════════════════════════════════════════════════════════════

        // GET: /Payment/RecordPayment
        public IActionResult RecordPayment()
        {
            return View(new RecordPaymentViewModel());
        }

        // GET: /Payment/SearchClientPolicies?clientId=1
        // Returns policies for a given client (for the policy picker dropdown)
        [HttpGet]
        public async Task<JsonResult> SearchClientPolicies(int clientId)
        {
            var policies = await _db.Policies
                .Include(p => p.Vehicle)
                .Include(p => p.PremiumLedger)
                .Where(p => p.ClientId == clientId
                         && (p.PolicyStatus == "Active"
                          || p.PolicyStatus == "Quoted"
                          || p.PolicyStatus == "Draft"))
                .OrderByDescending(p => p.PolicyId)
                .Select(p => new
                {
                    Id = p.PolicyId,
                    Text = p.CoverNoteNumber
                         + (p.Vehicle != null
                             ? " — " + p.Vehicle.RegistrationNumber
                             : ""),
                    NetPremiumPayable = p.PremiumLedger != null
                        ? p.PremiumLedger.NetPremiumPayable : 0,
                    TotalPaid = _db.CustomerPaymentLedgers
                        .Where(c => c.PolicyId == p.PolicyId)
                        .Sum(c => (decimal?)c.AmountPaid) ?? 0
                })
                .ToListAsync();

            return Json(policies);
        }

        // POST: /Payment/SavePayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SavePayment(RecordPaymentViewModel model)
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
                var clientExists = await _db.Clients.AnyAsync(c => c.ClientId == model.ClientId);
                if (!clientExists)
                    return Json(new { success = false, message = "Client not found." });

                var policyExists = await _db.Policies.AnyAsync(p => p.PolicyId == model.PolicyId);
                if (!policyExists)
                    return Json(new { success = false, message = "Policy not found." });

                var payment = new CustomerPaymentLedger
                {
                    ClientId = model.ClientId,
                    PolicyId = model.PolicyId,
                    PaymentDate = model.PaymentDate,
                    PaymentMethod = model.PaymentMethod,
                    AmountPaid = model.AmountPaid,
                    BankOrIssueName = model.BankOrIssueName,
                    ReferenceNumber = model.ReferenceNumber,
                    IsClearedAndSettled = model.IsClearedAndSettled,
                    Remarks = model.Remarks,
                    ReceiptNumber = await GenerateReceiptNumberAsync(),
                    RecordedBy = User.Identity?.Name,
                    CreatedAt = DateTime.UtcNow
                };

                _db.CustomerPaymentLedgers.Add(payment);
                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Payment recorded. Receipt: {payment.ReceiptNumber}",
                    receiptNumber = payment.ReceiptNumber,
                    paymentId = payment.PaymentId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Payment");
                return Json(new { success = false, message = "Failed to save payment. " + ex.Message });
            }
        }

        // POST: /Payment/ToggleCleared — mark a payment as cleared/uncleared
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<JsonResult> ToggleCleared(int PaymentId)
        {
            try
            {
                var payment = await _db.CustomerPaymentLedgers.FindAsync(PaymentId);
                if (payment == null) return GridError("Payment not found.");

                payment.IsClearedAndSettled = !payment.IsClearedAndSettled;
                await _db.SaveChangesAsync();

                return GridResult(new[] { new { payment.PaymentId, payment.IsClearedAndSettled } }, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling cleared for Payment {Id}", PaymentId);
                return GridError("Failed to update payment. " + ex.Message);
            }
        }


        // ════════════════════════════════════════════════════════════════
        // SECTION: OUTSTANDING BALANCES
        // ════════════════════════════════════════════════════════════════

        // GET: /Payment/Outstanding
        [Authorize(Roles = "Admin,SeniorAgent")]
        public IActionResult Outstanding() => View();

        // POST: /Payment/OutstandingRead
        [HttpPost]
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<JsonResult> OutstandingRead(
            int page = 1, int pageSize = 15,
            string? searchText = null)
        {
            try
            {
                // Build a raw outstanding list in-memory since EF
                // can't translate the subquery aggregation to serverSide paging cleanly
                var raw = await _db.PremiumLedgers
                    .Include(pl => pl.Policy)
                        .ThenInclude(p => p!.Client)
                    .Include(pl => pl.Policy)
                        .ThenInclude(p => p!.Vehicle)
                    .Where(pl => pl.Policy!.PolicyStatus == "Active"
                              || pl.Policy!.PolicyStatus == "Quoted")
                    .Select(pl => new
                    {
                        pl.PolicyId,
                        CoverNoteNumber = pl.Policy!.CoverNoteNumber,
                        ClientName = pl.Policy!.Client!.ClientName,
                        ClientId = pl.Policy!.Client!.ClientId,
                        RegistrationNumber = pl.Policy!.Vehicle != null
                            ? pl.Policy!.Vehicle!.RegistrationNumber : null,
                        PolicyStatus = pl.Policy!.PolicyStatus,
                        pl.NetPremiumPayable,
                        TotalPaid = _db.CustomerPaymentLedgers
                            .Where(c => c.PolicyId == pl.PolicyId)
                            .Sum(c => (decimal?)c.AmountPaid) ?? 0
                    })
                    .ToListAsync();

                // Filter to only records with a balance remaining
                var outstanding = raw
                    .Select(x => new
                    {
                        x.PolicyId,
                        x.CoverNoteNumber,
                        x.ClientName,
                        x.ClientId,
                        x.RegistrationNumber,
                        x.PolicyStatus,
                        x.NetPremiumPayable,
                        x.TotalPaid,
                        OutstandingBalance = x.NetPremiumPayable - x.TotalPaid
                    })
                    .Where(x => x.OutstandingBalance > 0)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var s = searchText.Trim();
                    outstanding = outstanding.Where(x =>
                        x.CoverNoteNumber.Contains(s) ||
                        x.ClientName.Contains(s));
                }

                var total = outstanding.Count();

                var data = outstanding
                    .OrderByDescending(x => x.OutstandingBalance)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Outstanding Balances");
                return GridError("Failed to load outstanding balances.");
            }
        }
    }
}