namespace ImsAgency.Web.Models.ViewModels
{
    // Shared filter model used across all reports
    public class ReportFilterViewModel
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? InsurerId { get; set; }
        public string? PolicyClassCode { get; set; }
        public string? AgentCode { get; set; }
        public string? PolicyStatus { get; set; }
    }

    // Summary cards shown above each report grid
    public class ReportSummaryViewModel
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string CardClass { get; set; } = "stat-card";
    }
}