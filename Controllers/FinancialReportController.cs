using ClosedXML.Excel;
using ImsAgency.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize(Roles = "Admin,SeniorAgent")]
    public class FinancialReportController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<FinancialReportController> _logger;

        public FinancialReportController(ApplicationDbContext db,
            ILogger<FinancialReportController> logger)
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
                { ["error"] = new { errors = new[] { message } } }
            }, _jsonOptions);

        private FileResult ExcelFile(XLWorkbook wb, string fileName)
        {
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        private static void StyleHeader(IXLWorksheet ws, int row, int cols)
        {
            var r = ws.Range(row, 1, row, cols);
            r.Style.Font.Bold = true;
            r.Style.Font.FontColor = XLColor.White;
            r.Style.Fill.BackgroundColor = XLColor.FromHtml("#0d6fa4");
        }


        // ════════════════════════════════════════════════════════════════
        // R5: PREMIUM COLLECTION
        // ════════════════════════════════════════════════════════════════

        // GET: /FinancialReport/PremiumCollection
        public IActionResult PremiumCollection() => View();

        // POST: /FinancialReport/PremiumCollectionData
        [HttpPost]
        public async Task<JsonResult> PremiumCollectionData(
            int page = 1, int pageSize = 20,
            string? dateFrom = null, string? dateTo = null,
            string? paymentMethod = null)
        {
            try
            {
                var query = _db.CustomerPaymentLedgers
                    .Include(p => p.Client)
                    .Include(p => p.Policy)
                        .ThenInclude(pol => pol!.Insurer)
                    .AsQueryable();

                if (DateTime.TryParse(dateFrom, out var dfrom))
                    query = query.Where(p => p.PaymentDate >= dfrom);
                if (DateTime.TryParse(dateTo, out var dto))
                    query = query.Where(p => p.PaymentDate <= dto);
                if (!string.IsNullOrWhiteSpace(paymentMethod) && paymentMethod != "All")
                    query = query.Where(p => p.PaymentMethod == paymentMethod);

                var total = await query.CountAsync();

                var data = await query
                    .OrderByDescending(p => p.PaymentDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.PaymentId,
                        p.ReceiptNumber,
                        ClientName = p.Client!.ClientName,
                        CoverNoteNumber = p.Policy!.CoverNoteNumber,
                        InsurerName = p.Policy!.Insurer!.InsurerName,
                        p.PaymentDate,
                        p.PaymentMethod,
                        p.AmountPaid,
                        p.IsClearedAndSettled,
                        p.RecordedBy
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PremiumCollection report");
                return GridError("Failed to load report.");
            }
        }

        // POST: /FinancialReport/PremiumCollectionExcel
        [HttpPost]
        public async Task<IActionResult> PremiumCollectionExcel(
            string? dateFrom = null, string? dateTo = null,
            string? paymentMethod = null)
        {
            var query = _db.CustomerPaymentLedgers
                .Include(p => p.Client)
                .Include(p => p.Policy)
                    .ThenInclude(pol => pol!.Insurer)
                .AsQueryable();

            if (DateTime.TryParse(dateFrom, out var dfrom)) query = query.Where(p => p.PaymentDate >= dfrom);
            if (DateTime.TryParse(dateTo, out var dto)) query = query.Where(p => p.PaymentDate <= dto);
            if (!string.IsNullOrWhiteSpace(paymentMethod) && paymentMethod != "All")
                query = query.Where(p => p.PaymentMethod == paymentMethod);

            var data = await query.OrderByDescending(p => p.PaymentDate).ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Premium Collection");
            var headers = new[] { "Receipt No.", "Client", "Cover Note",
                "Insurer", "Date", "Method", "Amount (RM)", "Cleared", "Recorded By" };

            ws.Cell(1, 1).Value = "IMS Agency — Premium Collection Report";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Range(1, 1, 1, headers.Length).Merge();

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(3, i + 1).Value = headers[i];
            StyleHeader(ws, 3, headers.Length);

            int row = 4;
            foreach (var p in data)
            {
                ws.Cell(row, 1).Value = p.ReceiptNumber;
                ws.Cell(row, 2).Value = p.Client?.ClientName ?? "";
                ws.Cell(row, 3).Value = p.Policy?.CoverNoteNumber ?? "";
                ws.Cell(row, 4).Value = p.Policy?.Insurer?.InsurerName ?? "";
                ws.Cell(row, 5).Value = p.PaymentDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 6).Value = p.PaymentMethod;
                ws.Cell(row, 7).Value = (double)p.AmountPaid;
                ws.Cell(row, 8).Value = p.IsClearedAndSettled ? "Yes" : "No";
                ws.Cell(row, 9).Value = p.RecordedBy ?? "";
                row++;
            }

            // Grand total
            ws.Cell(row, 6).Value = "TOTAL:";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).Value = (double)data.Sum(p => p.AmountPaid);
            ws.Cell(row, 7).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();
            return ExcelFile(wb, $"PremiumCollection_{DateTime.Now:yyyyMMdd}.xlsx");
        }


        // ════════════════════════════════════════════════════════════════
        // R6: COMMISSION REPORT
        // ════════════════════════════════════════════════════════════════

        // GET: /FinancialReport/CommissionReport
        public IActionResult CommissionReport() => View();

        // POST: /FinancialReport/CommissionReportData
        [HttpPost]
        public async Task<JsonResult> CommissionReportData(
            int page = 1, int pageSize = 20,
            string? dateFrom = null, string? dateTo = null,
            string? agentCode = null)
        {
            try
            {
                var query = _db.PremiumLedgers
                    .Include(pl => pl.Policy)
                        .ThenInclude(p => p!.Client)
                    .Include(pl => pl.Policy)
                        .ThenInclude(p => p!.Insurer)
                    .Where(pl => pl.AgentCommission.HasValue && pl.AgentCommission > 0)
                    .AsQueryable();

                if (DateTime.TryParse(dateFrom, out var dfrom))
                    query = query.Where(pl => pl.Policy!.StartDate >= dfrom);
                if (DateTime.TryParse(dateTo, out var dto))
                    query = query.Where(pl => pl.Policy!.StartDate <= dto);
                if (!string.IsNullOrWhiteSpace(agentCode) && agentCode != "All")
                    query = query.Where(pl => pl.Policy!.AgentCode == agentCode);

                var total = await query.CountAsync();

                var data = await query
                    .OrderByDescending(pl => pl.Policy!.StartDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(pl => new
                    {
                        CoverNoteNumber = pl.Policy!.CoverNoteNumber,
                        ClientName = pl.Policy!.Client!.ClientName,
                        InsurerName = pl.Policy!.Insurer!.InsurerName,
                        StartDate = pl.Policy!.StartDate,
                        pl.NetPremiumPayable,
                        pl.AgentCommission,
                        AgentCode = pl.Policy!.AgentCode
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CommissionReport");
                return GridError("Failed to load report.");
            }
        }

        // POST: /FinancialReport/CommissionReportExcel
        [HttpPost]
        public async Task<IActionResult> CommissionReportExcel(
            string? dateFrom = null, string? dateTo = null,
            string? agentCode = null)
        {
            var query = _db.PremiumLedgers
                .Include(pl => pl.Policy)
                    .ThenInclude(p => p!.Client)
                .Include(pl => pl.Policy)
                    .ThenInclude(p => p!.Insurer)
                .Where(pl => pl.AgentCommission.HasValue && pl.AgentCommission > 0)
                .AsQueryable();

            if (DateTime.TryParse(dateFrom, out var dfrom))
                query = query.Where(pl => pl.Policy!.StartDate >= dfrom);
            if (DateTime.TryParse(dateTo, out var dto))
                query = query.Where(pl => pl.Policy!.StartDate <= dto);
            if (!string.IsNullOrWhiteSpace(agentCode) && agentCode != "All")
                query = query.Where(pl => pl.Policy!.AgentCode == agentCode);

            var data = await query.OrderByDescending(pl => pl.Policy!.StartDate).ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Commission Report");
            var headers = new[] { "Cover Note", "Client", "Insurer",
                "Start Date", "Net Premium (RM)", "Commission (RM)", "Agent" };

            ws.Cell(1, 1).Value = "IMS Agency — Commission Report";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Range(1, 1, 1, headers.Length).Merge();

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(3, i + 1).Value = headers[i];
            StyleHeader(ws, 3, headers.Length);

            int row = 4;
            foreach (var p in data)
            {
                ws.Cell(row, 1).Value = p.Policy?.CoverNoteNumber ?? "";
                ws.Cell(row, 2).Value = p.Policy?.Client?.ClientName ?? "";
                ws.Cell(row, 3).Value = p.Policy?.Insurer?.InsurerName ?? "";
                ws.Cell(row, 4).Value = p.Policy?.StartDate.ToString("dd/MM/yyyy") ?? "";
                ws.Cell(row, 5).Value = (double)p.NetPremiumPayable;
                ws.Cell(row, 6).Value = (double)(p.AgentCommission ?? 0);
                ws.Cell(row, 7).Value = p.Policy?.AgentCode ?? "";
                row++;
            }

            ws.Cell(row, 5).Value = "TOTAL:";
            ws.Cell(row, 5).Style.Font.Bold = true;
            ws.Cell(row, 6).Value = (double)data.Sum(p => p.AgentCommission ?? 0);
            ws.Cell(row, 6).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();
            return ExcelFile(wb, $"CommissionReport_{DateTime.Now:yyyyMMdd}.xlsx");
        }


        // ════════════════════════════════════════════════════════════════
        // R7: OUTSTANDING PREMIUM REPORT
        // ════════════════════════════════════════════════════════════════

        // GET: /FinancialReport/OutstandingPremium
        public IActionResult OutstandingPremium() => View();

        // POST: /FinancialReport/OutstandingPremiumData
        [HttpPost]
        public async Task<JsonResult> OutstandingPremiumData(
            int page = 1, int pageSize = 20,
            string? searchText = null)
        {
            try
            {
                var raw = await _db.PremiumLedgers
                    .Include(pl => pl.Policy)
                        .ThenInclude(p => p!.Client)
                    .Include(pl => pl.Policy)
                        .ThenInclude(p => p!.Insurer)
                    .Include(pl => pl.Policy)
                        .ThenInclude(p => p!.Vehicle)
                    .Where(pl => pl.Policy!.PolicyStatus == "Active"
                              || pl.Policy!.PolicyStatus == "Quoted")
                    .Select(pl => new
                    {
                        pl.PolicyId,
                        CoverNoteNumber = pl.Policy!.CoverNoteNumber,
                        ClientName = pl.Policy!.Client!.ClientName,
                        InsurerName = pl.Policy!.Insurer!.InsurerName,
                        RegistrationNumber = pl.Policy!.Vehicle != null
                            ? pl.Policy!.Vehicle!.RegistrationNumber : null,
                        PolicyStatus = pl.Policy!.PolicyStatus,
                        pl.NetPremiumPayable,
                        TotalPaid = _db.CustomerPaymentLedgers
                            .Where(c => c.PolicyId == pl.PolicyId)
                            .Sum(c => (decimal?)c.AmountPaid) ?? 0
                    })
                    .ToListAsync();

                var outstanding = raw
                    .Select(x => new
                    {
                        x.PolicyId,
                        x.CoverNoteNumber,
                        x.ClientName,
                        x.InsurerName,
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
                _logger.LogError(ex, "Error in OutstandingPremium report");
                return GridError("Failed to load report.");
            }
        }

        // POST: /FinancialReport/OutstandingPremiumExcel
        [HttpPost]
        public async Task<IActionResult> OutstandingPremiumExcel()
        {
            var raw = await _db.PremiumLedgers
                .Include(pl => pl.Policy)
                    .ThenInclude(p => p!.Client)
                .Include(pl => pl.Policy)
                    .ThenInclude(p => p!.Insurer)
                .Where(pl => pl.Policy!.PolicyStatus == "Active"
                          || pl.Policy!.PolicyStatus == "Quoted")
                .Select(pl => new
                {
                    CoverNoteNumber = pl.Policy!.CoverNoteNumber,
                    ClientName = pl.Policy!.Client!.ClientName,
                    InsurerName = pl.Policy!.Insurer!.InsurerName,
                    pl.NetPremiumPayable,
                    TotalPaid = _db.CustomerPaymentLedgers
                        .Where(c => c.PolicyId == pl.PolicyId)
                        .Sum(c => (decimal?)c.AmountPaid) ?? 0
                })
                .ToListAsync();

            var data = raw
                .Where(x => (x.NetPremiumPayable - x.TotalPaid) > 0)
                .Select(x => new
                {
                    x.CoverNoteNumber,
                    x.ClientName,
                    x.InsurerName,
                    x.NetPremiumPayable,
                    x.TotalPaid,
                    Outstanding = x.NetPremiumPayable - x.TotalPaid
                })
                .OrderByDescending(x => x.Outstanding)
                .ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Outstanding Premium");
            var headers = new[] { "Cover Note", "Client", "Insurer",
                "Net Premium (RM)", "Total Paid (RM)", "Outstanding (RM)" };

            ws.Cell(1, 1).Value = "IMS Agency — Outstanding Premium Report";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Range(1, 1, 1, headers.Length).Merge();

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(3, i + 1).Value = headers[i];
            StyleHeader(ws, 3, headers.Length);

            int row = 4;
            foreach (var d in data)
            {
                ws.Cell(row, 1).Value = d.CoverNoteNumber;
                ws.Cell(row, 2).Value = d.ClientName;
                ws.Cell(row, 3).Value = d.InsurerName;
                ws.Cell(row, 4).Value = (double)d.NetPremiumPayable;
                ws.Cell(row, 5).Value = (double)d.TotalPaid;
                ws.Cell(row, 6).Value = (double)d.Outstanding;
                ws.Cell(row, 6).Style.Font.FontColor = XLColor.Red;
                row++;
            }

            ws.Cell(row, 5).Value = "TOTAL OUTSTANDING:";
            ws.Cell(row, 5).Style.Font.Bold = true;
            ws.Cell(row, 6).Value = (double)data.Sum(d => d.Outstanding);
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 6).Style.Font.FontColor = XLColor.Red;

            ws.Columns().AdjustToContents();
            return ExcelFile(wb, $"OutstandingPremium_{DateTime.Now:yyyyMMdd}.xlsx");
        }
    }
}