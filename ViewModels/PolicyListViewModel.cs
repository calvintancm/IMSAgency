namespace ImsAgency.Web.Models.ViewModels
{
    public class PolicyListViewModel
    {
        public int TotalMotorPolicies { get; set; }
        public int ActiveMotorPolicies { get; set; }
        public int ExpiringIn30Days { get; set; }
        public decimal TotalSumInsured { get; set; }
    }

    public class ExpiredPolicyListViewModel
    {
        public int TotalExpiredMotor { get; set; }
        public int PendingRenewalQuotes { get; set; } // Expired policies that already have a renewal quote (IsRenewal child exists)
    }
}