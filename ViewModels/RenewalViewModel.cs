namespace ImsAgency.Web.Models.ViewModels
{
    public class RenewalListViewModel
    {
        public int TotalDue7Days { get; set; }
        public int TotalDue30Days { get; set; }
        public int TotalDue60Days { get; set; }
        public int TotalDue90Days { get; set; }
        public int TotalExpired { get; set; }
        public int TotalNoticesSentToday { get; set; }
    }

    // Used by the WhatsApp simulator send endpoint
    public class SendReminderViewModel
    {
        public int PolicyId { get; set; }
        public string NoticeType { get; set; } = string.Empty;
        public string Channel { get; set; } = "WhatsApp";
        public string PhoneOrEmail { get; set; } = string.Empty;
        public string MessageContent { get; set; } = string.Empty;
        public string? AgentNote { get; set; }
    }
}