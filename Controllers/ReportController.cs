using ClosedXML.Excel;
using ImsAgency.Web.Data;
using ImsAgency.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize]
    public class ReportController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ReportController> _logger;

        public ReportController(ApplicationDbContext db, ILogger<ReportController> logger)
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
        // SHARED: Load filter dropdowns
        // ================================================================
        private async Task LoadReportDropdownsAsync()
        {
            ViewBag.Insurers = await _db.Insurers
                .Where(i => i.IsActive)
                .OrderBy(i => i.InsurerName)
                .Select(i => new { i.InsurerId, i.InsurerName })
                .ToListAsync();

            ViewBag.PolicyClasses = await _db.PolicyClasses
                .Where(pc => pc.IsActive)
                .OrderBy(pc => pc.DisplayOrder)
                .Select(pc => new { pc.ClassCode, pc.ClassName })
                .ToListAsync();

            ViewBag.Agents = await _db.AgentProfiles
                .Where(a => a.IsActive)
                .OrderBy(a => a.FullName)
                .Select(a => new { a.AgentCode, a.FullName })
                .ToListAsync();
        }

        // ================================================================
        // SHARED: Excel workbook styling helper
        // ================================================================
        private static void StyleHeaderRow(IXLWorksheet ws, int row, int colCount,
            string hexColor = "#0d6fa4")
        {
            var range = ws.Range(row, 1, row, colCount);
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = XLColor.White;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml(hexColor);
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void AddReportHeader(IXLWorksheet ws, string title,
            string subTitle, int colCount)
        {
            ws.Cell(1, 1).Value = "IMS Agency";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, colCount).Merge();

            ws.Cell(2, 1).Value = title;
            ws.Cell(2, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Style.Font.FontSize = 12;
            ws.Range(2, 1, 2, colCount).Merge();

            ws.Cell(3, 1).Value = subTitle;
            ws.Range(3, 1, 3, colCount).Merge();

            ws.Cell(4, 1).Value = $"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}";
            ws.Range(4, 1, 4, colCount).Merge();
        }

        private FileResult ExcelFile(XLWorkbook wb, string fileName)
        {
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }


        // ════════════════════════════════════════════════════════════════
        // R1: POLICY SUMMARY
        // ════════════════════════════════════════════════════════════════

        // GET: /Report/PolicySummary
        public async Task<IActionResult> PolicySummary()
        {
            await LoadReportDropdownsAsync();
            return View();
        }

        // POST: /Report/PolicySummaryData
        [HttpPost]
        public async Task<JsonResult> PolicySummaryData(
            int page = 1, int pageSize = 20,
            string? dateFrom = null, string? dateTo = null,
            int? insurerId = null, string? policyClassCode = null,
            string? status = null, string? agentCode = null)
        {
            try
            {
                var query = _db.Policies
                    .Include(p => p.Client)
                    .Include(p => p.Vehicle)
                    .Include(p => p.Insurer)
                    .Include(p => p.PolicyClass)
                    .Include(p => p.PremiumLedger)
                    .AsQueryable();

                if (DateTime.TryParse(dateFrom, out var dfrom))
                    query = query.Where(p => p.StartDate >= dfrom);
                if (DateTime.TryParse(dateTo, out var dto))
                    query = query.Where(p => p.StartDate <= dto);
                if (insurerId.HasValue && insurerId > 0)
                    query = query.Where(p => p.InsurerId == insurerId.Value);
                if (!string.IsNullOrWhiteSpace(policyClassCode) && policyClassCode != "All")
                    query = query.Where(p => p.PolicyClassCode == policyClassCode);
                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                    query = query.Where(p => p.PolicyStatus == status);
                if (!string.IsNullOrWhiteSpace(agentCode) && agentCode != "All")
                    query = query.Where(p => p.AgentCode == agentCode);

                var total = await query.CountAsync();

                var data = await query
                    .OrderByDescending(p => p.StartDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.PolicyId,
                        p.CoverNoteNumber,
                        p.PolicyNumber,
                        ClientName = p.Client!.ClientName,
                        RegistrationNumber = p.Vehicle != null
                            ? p.Vehicle.RegistrationNumber : null,
                        InsurerName = p.Insurer!.InsurerName,
                        PolicyClassName = p.PolicyClass!.ClassName,
                        p.PolicyStatus,
                        p.StartDate,
                        p.ExpiryDate,
                        p.NcdPercentage,
                        NetPremiumPayable = p.PremiumLedger != null
                            ? p.PremiumLedger.NetPremiumPayable : 0,
                        p.AgentCode
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading PolicySummary");
                return GridError("Failed to load report.");
            }
        }

        // POST: /Report/PolicySummaryExcel
        [HttpPost]
        public async Task<IActionResult> PolicySummaryExcel(
            string? dateFrom = null, string? dateTo = null,
            int? insurerId = null, string? policyClassCode = null,
            string? status = null, string? agentCode = null)
        {
            var query = _db.Policies
                .Include(p => p.Client)
                .Include(p => p.Vehicle)
                .Include(p => p.Insurer)
                .Include(p => p.PolicyClass)
                .Include(p => p.PremiumLedger)
                .AsQueryable();

            if (DateTime.TryParse(dateFrom, out var dfrom)) query = query.Where(p => p.StartDate >= dfrom);
            if (DateTime.TryParse(dateTo, out var dto)) query = query.Where(p => p.StartDate <= dto);
            if (insurerId.HasValue && insurerId > 0) query = query.Where(p => p.InsurerId == insurerId.Value);
            if (!string.IsNullOrWhiteSpace(policyClassCode) && policyClassCode != "All")
                query = query.Where(p => p.PolicyClassCode == policyClassCode);
            if (!string.IsNullOrWhiteSpace(status) && status != "All")
                query = query.Where(p => p.PolicyStatus == status);
            if (!string.IsNullOrWhiteSpace(agentCode) && agentCode != "All")
                query = query.Where(p => p.AgentCode == agentCode);

            var data = await query
                .OrderByDescending(p => p.StartDate)
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Policy Summary");
            var headers = new[] { "Cover Note", "Policy No.", "Client", "Registration",
                "Insurer", "Class", "Status", "Start Date", "Expiry Date",
                "NCD %", "Net Premium (RM)", "Agent" };

            AddReportHeader(ws, "Policy Summary Report",
                $"Period: {dateFrom ?? "All"} to {dateTo ?? "All"}", headers.Length);

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(6, i + 1).Value = headers[i];
            StyleHeaderRow(ws, 6, headers.Length);

            int row = 7;
            foreach (var p in data)
            {
                ws.Cell(row, 1).Value = p.CoverNoteNumber;
                ws.Cell(row, 2).Value = p.PolicyNumber ?? "";
                ws.Cell(row, 3).Value = p.Client?.ClientName ?? "";
                ws.Cell(row, 4).Value = p.Vehicle?.RegistrationNumber ?? "";
                ws.Cell(row, 5).Value = p.Insurer?.InsurerName ?? "";
                ws.Cell(row, 6).Value = p.PolicyClass?.ClassName ?? "";
                ws.Cell(row, 7).Value = p.PolicyStatus;
                ws.Cell(row, 8).Value = p.StartDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 9).Value = p.ExpiryDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 10).Value = (double)p.NcdPercentage;
                ws.Cell(row, 11).Value = (double)(p.PremiumLedger?.NetPremiumPayable ?? 0);
                ws.Cell(row, 12).Value = p.AgentCode ?? "";
                row++;
            }

            ws.Cell(row, 10).Value = "TOTAL:";
            ws.Cell(row, 10).Style.Font.Bold = true;
            ws.Cell(row, 11).Value = (double)data.Sum(p => p.PremiumLedger?.NetPremiumPayable ?? 0);
            ws.Cell(row, 11).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();
            return ExcelFile(wb, $"PolicySummary_{DateTime.Now:yyyyMMdd}.xlsx");
        }


        // ════════════════════════════════════════════════════════════════
        // R2: EXPIRY SCHEDULE
        // ════════════════════════════════════════════════════════════════

        // GET: /Report/ExpirySchedule
        public async Task<IActionResult> ExpirySchedule()
        {
            await LoadReportDropdownsAsync();
            return View();
        }

        // POST: /Report/ExpiryScheduleData
        [HttpPost]
        public async Task<JsonResult> ExpiryScheduleData(
            int page = 1, int pageSize = 20,
            int expiryDays = 90, string? insurerId2 = null,
            string? policyClassCode = null)
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                var toDate = today.AddDays(expiryDays);

                var query = _db.Policies
                    .Include(p => p.Client)
                    .Include(p => p.Vehicle)
                    .Include(p => p.Insurer)
                    .Include(p => p.PolicyClass)
                    .Include(p => p.PremiumLedger)
                    .Where(p => p.PolicyStatus == "Active"
                             && p.ExpiryDate >= today
                             && p.ExpiryDate <= toDate)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(insurerId2) && insurerId2 != "0"
                    && int.TryParse(insurerId2, out var insId))
                    query = query.Where(p => p.InsurerId == insId);

                if (!string.IsNullOrWhiteSpace(policyClassCode) && policyClassCode != "All")
                    query = query.Where(p => p.PolicyClassCode == policyClassCode);

                var total = await query.CountAsync();

                var rawData = await query
                    .OrderBy(p => p.ExpiryDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.PolicyId,
                        p.CoverNoteNumber,
                        ClientName = p.Client!.ClientName,
                        RegistrationNumber = p.Vehicle != null
                            ? p.Vehicle.RegistrationNumber : null,
                        InsurerName = p.Insurer!.InsurerName,
                        PolicyClassName = p.PolicyClass!.ClassName,
                        p.ExpiryDate,
                        NetPremiumPayable = p.PremiumLedger != null
                            ? p.PremiumLedger.NetPremiumPayable : 0,
                        p.RenewalReminderCount,
                        p.AgentCode
                    })
                    .ToListAsync();

                var data = rawData.Select(p => new
                {
                    p.PolicyId,
                    p.CoverNoteNumber,
                    p.ClientName,
                    p.RegistrationNumber,
                    p.InsurerName,
                    p.PolicyClassName,
                    p.ExpiryDate,
                    p.NetPremiumPayable,
                    p.RenewalReminderCount,
                    p.AgentCode,
                    DaysToExpiry = (p.ExpiryDate.Date - today).Days
                });

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading ExpirySchedule");
                return GridError("Failed to load report.");
            }
        }

        // POST: /Report/ExpiryScheduleExcel
        [HttpPost]
        public async Task<IActionResult> ExpiryScheduleExcel(int expiryDays = 90)
        {
            var today = DateTime.UtcNow.Date;
            var toDate = today.AddDays(expiryDays);

            var data = await _db.Policies
                .Include(p => p.Client)
                .Include(p => p.Vehicle)
                .Include(p => p.Insurer)
                .Include(p => p.PolicyClass)
                .Where(p => p.PolicyStatus == "Active"
                         && p.ExpiryDate >= today
                         && p.ExpiryDate <= toDate)
                .OrderBy(p => p.ExpiryDate)
                .ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Expiry Schedule");
            var headers = new[] { "Cover Note", "Client", "Registration",
                "Insurer", "Class", "Expiry Date", "Days Left",
                "Net Premium (RM)", "Reminders Sent", "Agent" };

            AddReportHeader(ws, "Policy Expiry Schedule",
                $"Next {expiryDays} days — Generated {DateTime.Now:dd/MM/yyyy}", headers.Length);

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(6, i + 1).Value = headers[i];
            StyleHeaderRow(ws, 6, headers.Length);

            int row = 7;
            foreach (var p in data)
            {
                var daysLeft = (p.ExpiryDate.Date - today).Days;
                ws.Cell(row, 1).Value = p.CoverNoteNumber;
                ws.Cell(row, 2).Value = p.Client?.ClientName ?? "";
                ws.Cell(row, 3).Value = p.Vehicle?.RegistrationNumber ?? "";
                ws.Cell(row, 4).Value = p.Insurer?.InsurerName ?? "";
                ws.Cell(row, 5).Value = p.PolicyClass?.ClassName ?? "";
                ws.Cell(row, 6).Value = p.ExpiryDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 7).Value = daysLeft;
                ws.Cell(row, 8).Value = 0; // PremiumLedger not loaded here — add include if needed
                ws.Cell(row, 9).Value = p.RenewalReminderCount;
                ws.Cell(row, 10).Value = p.AgentCode ?? "";

                // Color code by urgency
                if (daysLeft <= 30)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEE2E2");
                else if (daysLeft <= 60)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF3C7");

                row++;
            }

            ws.Columns().AdjustToContents();
            return ExcelFile(wb, $"ExpirySchedule_{DateTime.Now:yyyyMMdd}.xlsx");
        }


        // ════════════════════════════════════════════════════════════════
        // R3: AGENT PERFORMANCE
        // ════════════════════════════════════════════════════════════════

        // GET: /Report/AgentPerformance
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<IActionResult> AgentPerformance()
        {
            await LoadReportDropdownsAsync();
            return View();
        }

        // POST: /Report/AgentPerformanceData
        [HttpPost]
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<JsonResult> AgentPerformanceData(
            string? dateFrom = null, string? dateTo = null)
        {
            try
            {
                var query = _db.Policies
                    .Include(p => p.PremiumLedger)
                    .Where(p => p.AgentCode != null)
                    .AsQueryable();

                if (DateTime.TryParse(dateFrom, out var dfrom))
                    query = query.Where(p => p.StartDate >= dfrom);
                if (DateTime.TryParse(dateTo, out var dto))
                    query = query.Where(p => p.StartDate <= dto);

                var rawData = await query.ToListAsync();

                var data = rawData
                    .GroupBy(p => p.AgentCode!)
                    .Select(g => new
                    {
                        AgentCode = g.Key,
                        TotalPolicies = g.Count(),
                        ActivePolicies = g.Count(p => p.PolicyStatus == "Active"),
                        TotalPremium = g.Sum(p => p.PremiumLedger?.NetPremiumPayable ?? 0),
                        TotalCommission = g.Sum(p => p.PremiumLedger?.AgentCommission ?? 0),
                        MotorCount = g.Count(p =>
                            new[] { "MTRCAR", "MTRBIKE", "MTRCV" }.Contains(p.PolicyClassCode)),
                        NonMotorCount = g.Count(p =>
                            !new[] { "MTRCAR", "MTRBIKE", "MTRCV" }.Contains(p.PolicyClassCode))
                    })
                    .OrderByDescending(x => x.TotalPremium)
                    .ToList();

                return GridResult(data, data.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading AgentPerformance");
                return GridError("Failed to load report.");
            }
        }

        // POST: /Report/AgentPerformanceExcel
        [HttpPost]
        [Authorize(Roles = "Admin,SeniorAgent")]
        public async Task<IActionResult> AgentPerformanceExcel(
            string? dateFrom = null, string? dateTo = null)
        {
            var query = _db.Policies
                .Include(p => p.PremiumLedger)
                .Where(p => p.AgentCode != null).AsQueryable();

            if (DateTime.TryParse(dateFrom, out var dfrom)) query = query.Where(p => p.StartDate >= dfrom);
            if (DateTime.TryParse(dateTo, out var dto)) query = query.Where(p => p.StartDate <= dto);

            var rawData = await query.ToListAsync();
            var data = rawData.GroupBy(p => p.AgentCode!)
                .Select(g => new
                {
                    AgentCode = g.Key,
                    TotalPolicies = g.Count(),
                    ActivePolicies = g.Count(p => p.PolicyStatus == "Active"),
                    TotalPremium = g.Sum(p => p.PremiumLedger?.NetPremiumPayable ?? 0),
                    TotalCommission = g.Sum(p => p.PremiumLedger?.AgentCommission ?? 0),
                    MotorCount = g.Count(p =>
                        new[] { "MTRCAR", "MTRBIKE", "MTRCV" }.Contains(p.PolicyClassCode)),
                    NonMotorCount = g.Count(p =>
                        !new[] { "MTRCAR", "MTRBIKE", "MTRCV" }.Contains(p.PolicyClassCode))
                })
                .OrderByDescending(x => x.TotalPremium)
                .ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Agent Performance");
            var headers = new[] { "Agent Code", "Total Policies", "Active",
                "Motor", "Non-Motor", "Total Premium (RM)", "Total Commission (RM)" };

            AddReportHeader(ws, "Agent Performance Report",
                $"Period: {dateFrom ?? "All"} to {dateTo ?? "All"}", headers.Length);

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(6, i + 1).Value = headers[i];
            StyleHeaderRow(ws, 6, headers.Length);

            int row = 7;
            foreach (var d in data)
            {
                ws.Cell(row, 1).Value = d.AgentCode;
                ws.Cell(row, 2).Value = d.TotalPolicies;
                ws.Cell(row, 3).Value = d.ActivePolicies;
                ws.Cell(row, 4).Value = d.MotorCount;
                ws.Cell(row, 5).Value = d.NonMotorCount;
                ws.Cell(row, 6).Value = (double)d.TotalPremium;
                ws.Cell(row, 7).Value = (double)d.TotalCommission;
                row++;
            }

            ws.Columns().AdjustToContents();
            return ExcelFile(wb, $"AgentPerformance_{DateTime.Now:yyyyMMdd}.xlsx");
        }
    }
}