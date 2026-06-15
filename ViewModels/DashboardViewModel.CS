namespace ImsAgency.Web.Models.ViewModels
{
    public class DashboardViewModel
    {
        // ---- Top stat cards ----
        public int TotalActivePolicies { get; set; }
        public int TotalClients { get; set; }
        public decimal TotalPremiumCollected { get; set; }
        public int PendingClaimsCount { get; set; }

        // ---- Renewal pipeline counts (Feature 1) ----
        public int ExpiringIn7Days { get; set; }
        public int ExpiringIn30Days { get; set; }
        public int ExpiringIn60Days { get; set; }
        public int ExpiringIn90Days { get; set; }
        public int ExpiredCount { get; set; }

        // ---- Chart data ----
        public List<PolicyStatusSlice> PolicyStatusBreakdown { get; set; } = new();
        public List<PolicyClassSlice> PolicyClassBreakdown { get; set; } = new();

        // ---- Renewal pipeline table (Feature 1) ----
        public List<RenewalPipelineItem> UpcomingRenewals { get; set; } = new();

        // ---- Outstanding balances ----
        public decimal TotalOutstandingBalance { get; set; }
    }

    public class PolicyStatusSlice
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class PolicyClassSlice
    {
        public string ClassCategory { get; set; } = string.Empty; // "Motor" or "NonMotor"
        public int Count { get; set; }
    }

    public class RenewalPipelineItem
    {
        public int PolicyId { get; set; }
        public string CoverNoteNumber { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string? RegistrationNumber { get; set; }
        public string PolicyClassName { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int DaysToExpiry { get; set; }
    }
}