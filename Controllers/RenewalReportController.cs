using ClosedXML.Excel;
using ImsAgency.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImsAgency.Web.Controllers
{
    [Authorize]
    public class RenewalReportController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<RenewalReportController> _logger;

        public RenewalReportController(ApplicationDbContext db,
            ILogger<RenewalReportController> logger)
        {
            _db = db;
            _logger = logger;
        }

        private static readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { PropertyNamingPolicy = null };

        private JsonResult GridResult(object data, int total) =>
            Json(new { Data = data, Total = total, Errors = (object?)null }, _jsonOptions);

        private JsonResult GridError(string msg) =>
            Json(new
            {
                Data = Array.Empty<object>(),
                Total = 0,
                Errors = new Dictionary<string, object>
                { ["error"] = new { errors = new[] { msg } } }
            }, _jsonOptions);

        private FileResult ExcelFile(XLWorkbook wb, string fileName)
        {
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }


        // ════════════════════════════════════════════════════════════════
        // R8: RENEWAL RATE
        // ════════════════════════════════════════════════════════════════

        // GET: /RenewalReport/RenewalRate
        public IActionResult RenewalRate() => View();

        // POST: /RenewalReport/RenewalRateData
        [HttpPost]
        public async Task<JsonResult> RenewalRateData(int year = 0)
        {
            try
            {
                if (year == 0) year = DateTime.UtcNow.Year;

                // Get all expired policies from the given year
                var expired = await _db.Policies
                    .Where(p => p.PolicyStatus == "Expired"
                             && p.ExpiryDate.Year == year)
                    .Select(p => new { p.PolicyId, p.PolicyClassCode })
                    .ToListAsync();

                // Of those expired, count how many have a renewal
                var expiredIds = expired.Select(e => e.PolicyId).ToHashSet();

                var renewedCount = await _db.Policies
                    .CountAsync(p => p.PreviousPolicyId.HasValue
                                  && expiredIds.Contains(p.PreviousPolicyId!.Value));

                var totalExpired = expired.Count;
                var notRenewed = totalExpired - renewedCount;
                var renewalRate = totalExpired > 0
                    ? Math.Round((decimal)renewedCount / totalExpired * 100, 1)
                    : 0;

                // Break down by policy class
                var byClass = expired
                    .GroupBy(e => e.PolicyClassCode)
                    .Select(g => new
                    {
                        PolicyClassCode = g.Key,
                        TotalExpired = g.Count()
                    })
                    .ToList();

                var data = new[]
                {
                    new
                    {
                        Year         = year,
                        TotalExpired = totalExpired,
                        Renewed      = renewedCount,
                        NotRenewed   = notRenewed,
                        RenewalRate  = renewalRate
                    }
                };

                return GridResult(data, data.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RenewalRate report");
                return GridError("Failed to load report.");
            }
        }

        // POST: /RenewalReport/RenewalRateExcel
        [HttpPost]
        public async Task<IActionResult> RenewalRateExcel(int year = 0)
        {
            if (year == 0) year = DateTime.UtcNow.Year;

            var expired = await _db.Policies
                .Include(p => p.Client)
                .Include(p => p.PolicyClass)
                .Where(p => p.PolicyStatus == "Expired" && p.ExpiryDate.Year == year)
                .ToListAsync();

            var expiredIds = expired.Select(e => e.PolicyId).ToHashSet();
            var renewedIds = (await _db.Policies
             .Where(p => p.PreviousPolicyId.HasValue
                      && expiredIds.Contains(p.PreviousPolicyId!.Value))
             .Select(p => p.PreviousPolicyId!.Value)
             .ToListAsync())
             .ToHashSet();


            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Renewal Rate");
            var headers = new[] { "Cover Note", "Client", "Policy Class",
                "Expiry Date", "Renewed?" };

            ws.Cell(1, 1).Value = $"IMS Agency — Renewal Rate Report {year}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Range(1, 1, 1, headers.Length).Merge();

            var total = expired.Count;
            var renewed = renewedIds.Count;
            ws.Cell(2, 1).Value = $"Total Expired: {total} | Renewed: {renewed} "
                + $"| Rate: {(total > 0 ? (renewed * 100 / total) : 0)}%";
            ws.Range(2, 1, 2, headers.Length).Merge();

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(4, i + 1).Value = headers[i];
            var hRow = ws.Range(4, 1, 4, headers.Length);
            hRow.Style.Font.Bold = true;
            hRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#0d6fa4");
            hRow.Style.Font.FontColor = XLColor.White;

            int row = 5;
            foreach (var p in expired.OrderBy(p => p.ExpiryDate))
            {
                bool wasRenewed = renewedIds.Contains(p.PolicyId);
                ws.Cell(row, 1).Value = p.CoverNoteNumber;
                ws.Cell(row, 2).Value = p.Client?.ClientName ?? "";
                ws.Cell(row, 3).Value = p.PolicyClass?.ClassName ?? "";
                ws.Cell(row, 4).Value = p.ExpiryDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 5).Value = wasRenewed ? "Yes" : "No";
                if (!wasRenewed)
                    ws.Cell(row, 5).Style.Font.FontColor = XLColor.Red;
                else
                    ws.Cell(row, 5).Style.Font.FontColor = XLColor.FromHtml("#16a34a");
                row++;
            }

            ws.Columns().AdjustToContents();
            return ExcelFile(wb, $"RenewalRate_{year}.xlsx");
        }


        // ════════════════════════════════════════════════════════════════
        // R9: NOTICE HISTORY
        // ════════════════════════════════════════════════════════════════

        // GET: /RenewalReport/NoticeHistory
        public IActionResult NoticeHistory() => View();

        // POST: /RenewalReport/NoticeHistoryData
        [HttpPost]
        public async Task<JsonResult> NoticeHistoryData(
            int page = 1, int pageSize = 20,
            string? dateFrom = null, string? dateTo = null,
            string? channel = null)
        {
            try
            {
                var query = _db.RenewalNotices
                    .Include(n => n.Policy)
                        .ThenInclude(p => p!.Client)
                    .AsQueryable();

                if (DateTime.TryParse(dateFrom, out var dfrom))
                    query = query.Where(n => n.SentAt >= dfrom);
                if (DateTime.TryParse(dateTo, out var dto))
                    query = query.Where(n => n.SentAt <= dto);
                if (!string.IsNullOrWhiteSpace(channel) && channel != "All")
                    query = query.Where(n => n.Channel == channel);

                var total = await query.CountAsync();

                var data = await query
                    .OrderByDescending(n => n.SentAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(n => new
                    {
                        n.NoticeId,
                        n.PolicyId,
                        CoverNoteNumber = n.Policy!.CoverNoteNumber,
                        ClientName = n.Policy!.Client!.ClientName,
                        n.NoticeType,
                        n.SentAt,
                        n.Channel,
                        n.PhoneOrEmail,
                        n.IsDelivered,
                        n.AgentNote
                    })
                    .ToListAsync();

                return GridResult(data, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NoticeHistory report");
                return GridError("Failed to load report.");
            }
        }

        // POST: /RenewalReport/NoticeHistoryExcel
        [HttpPost]
        public async Task<IActionResult> NoticeHistoryExcel(
            string? dateFrom = null, string? dateTo = null,
            string? channel = null)
        {
            var query = _db.RenewalNotices
                .Include(n => n.Policy)
                    .ThenInclude(p => p!.Client)
                .AsQueryable();

            if (DateTime.TryParse(dateFrom, out var dfrom)) query = query.Where(n => n.SentAt >= dfrom);
            if (DateTime.TryParse(dateTo, out var dto)) query = query.Where(n => n.SentAt <= dto);
            if (!string.IsNullOrWhiteSpace(channel) && channel != "All")
                query = query.Where(n => n.Channel == channel);

            var data = await query.OrderByDescending(n => n.SentAt).ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Notice History");
            var headers = new[] { "Date Sent", "Cover Note", "Client",
                "Type", "Channel", "Recipient", "Delivered?", "Agent Note" };

            ws.Cell(1, 1).Value = "IMS Agency — Renewal Notice History";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Range(1, 1, 1, headers.Length).Merge();

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(3, i + 1).Value = headers[i];
            var hRow = ws.Range(3, 1, 3, headers.Length);
            hRow.Style.Font.Bold = true;
            hRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#0d6fa4");
            hRow.Style.Font.FontColor = XLColor.White;

            int row = 4;
            foreach (var n in data)
            {
                ws.Cell(row, 1).Value = n.SentAt.ToString("dd/MM/yyyy HH:mm");
                ws.Cell(row, 2).Value = n.Policy?.CoverNoteNumber ?? "";
                ws.Cell(row, 3).Value = n.Policy?.Client?.ClientName ?? "";
                ws.Cell(row, 4).Value = n.NoticeType;
                ws.Cell(row, 5).Value = n.Channel;
                ws.Cell(row, 6).Value = n.PhoneOrEmail;
                ws.Cell(row, 7).Value = n.IsDelivered ? "Yes" : "No";
                ws.Cell(row, 8).Value = n.AgentNote ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();
            return ExcelFile(wb, $"NoticeHistory_{DateTime.Now:yyyyMMdd}.xlsx");
        }
    }
}