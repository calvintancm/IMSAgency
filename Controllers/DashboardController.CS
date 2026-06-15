using ImsAgency.Web.Data;
using ImsAgency.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImsAgency.Web.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _db;

        public DashboardController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.UtcNow.Date;
            var vm = new DashboardViewModel();

            // ================================================================
            // TOP STAT CARDS
            // ================================================================
            vm.TotalActivePolicies = await _db.Policies
                .CountAsync(p => p.PolicyStatus == "Active");

            vm.TotalClients = await _db.Clients
                .CountAsync(c => c.IsActive);

            vm.TotalPremiumCollected = await _db.CustomerPaymentLedgers
                .SumAsync(p => (decimal?)p.AmountPaid) ?? 0;

            vm.PendingClaimsCount = await _db.Claims
                .CountAsync(c => c.ClaimStatus == "Lodged" || c.ClaimStatus == "InProgress");

            // ================================================================
            // RENEWAL PIPELINE COUNTS (Feature 1)
            // Each bucket counts policies expiring WITHIN that many days
            // (not exclusive ranges) — matches how "Due in 30 Days" etc.
            // are typically interpreted on a renewal dashboard.
            // ================================================================
            vm.ExpiringIn7Days = await _db.Policies.CountAsync(p =>
                p.PolicyStatus == "Active" && p.ExpiryDate >= today && p.ExpiryDate <= today.AddDays(7));

            vm.ExpiringIn30Days = await _db.Policies.CountAsync(p =>
                p.PolicyStatus == "Active" && p.ExpiryDate >= today && p.ExpiryDate <= today.AddDays(30));

            vm.ExpiringIn60Days = await _db.Policies.CountAsync(p =>
                p.PolicyStatus == "Active" && p.ExpiryDate >= today && p.ExpiryDate <= today.AddDays(60));

            vm.ExpiringIn90Days = await _db.Policies.CountAsync(p =>
                p.PolicyStatus == "Active" && p.ExpiryDate >= today && p.ExpiryDate <= today.AddDays(90));

            vm.ExpiredCount = await _db.Policies
                .CountAsync(p => p.PolicyStatus == "Expired");

            // ================================================================
            // CHART 1: POLICY STATUS BREAKDOWN (donut)
            // ================================================================
            vm.PolicyStatusBreakdown = await _db.Policies
                .GroupBy(p => p.PolicyStatus)
                .Select(g => new PolicyStatusSlice { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            // ================================================================
            // CHART 2: MOTOR vs NON-MOTOR BREAKDOWN (donut)
            // ================================================================
            vm.PolicyClassBreakdown = await _db.Policies
                .Include(p => p.PolicyClass)
                .Where(p => p.PolicyClass != null)
                .GroupBy(p => p.PolicyClass!.ClassCategory)
                .Select(g => new PolicyClassSlice { ClassCategory = g.Key, Count = g.Count() })
                .ToListAsync();

            // ================================================================
            // RENEWAL PIPELINE TABLE — next 8 policies expiring within 90 days
            // ================================================================
            var rawRenewals = await _db.Policies
                .Include(p => p.Client)
                .Include(p => p.Vehicle)
                .Include(p => p.PolicyClass)
                .Where(p => p.PolicyStatus == "Active" && p.ExpiryDate <= today.AddDays(90))
                .OrderBy(p => p.ExpiryDate)
                .Take(8)
                .Select(p => new
                {
                    p.PolicyId,
                    p.CoverNoteNumber,
                    ClientName = p.Client!.ClientName,
                    RegistrationNumber = p.Vehicle != null ? p.Vehicle.RegistrationNumber : null,
                    PolicyClassName = p.PolicyClass!.ClassName,
                    p.ExpiryDate
                })
                .ToListAsync();

            // DaysToExpiry is computed in-memory (DateTime subtraction
            // doesn't translate well to SQL across all providers)
            vm.UpcomingRenewals = rawRenewals.Select(r => new RenewalPipelineItem
            {
                PolicyId = r.PolicyId,
                CoverNoteNumber = r.CoverNoteNumber,
                ClientName = r.ClientName,
                RegistrationNumber = r.RegistrationNumber,
                PolicyClassName = r.PolicyClassName,
                ExpiryDate = r.ExpiryDate,
                DaysToExpiry = (r.ExpiryDate.Date - today).Days
            }).ToList();

            // ================================================================
            // OUTSTANDING BALANCES (NetPremiumPayable - AmountPaid, summed
            // across all policies with a positive balance)
            // ================================================================
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

            vm.TotalOutstandingBalance = ledgerTotals
                .Sum(x => Math.Max(0, x.NetPremiumPayable - x.TotalPaid));

            return View(vm);
        }
    }
}