namespace ImsAgency.Web.Models.ViewModels
{
    // Powers the Client 360° View (Feature 8)
    public class ClientProfileViewModel
    {
        public int ClientId { get; set; }
        public string ClientCode { get; set; } = string.Empty;
        public string ClientType { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string IdentificationNumber { get; set; } = string.Empty;
        public string IdentificationType { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string? EmailAddress { get; set; }
        public string? OccupationOrBusiness { get; set; }
        public bool IsActive { get; set; }
        public bool IsBlacklisted { get; set; }
        public string? Remarks { get; set; }
        public DateTime CreatedAt { get; set; }

        public List<ClientPhoneItem> Phones { get; set; } = new();
        public List<ClientAddressItem> Addresses { get; set; } = new();
        public List<PolicyTimelineItem> Policies { get; set; } = new();
        public List<PaymentHistoryItem> Payments { get; set; } = new();
        public List<ClaimHistoryItem> Claims { get; set; } = new();
        public List<RenewalNoticeItem> RenewalNotices { get; set; } = new();

        // ---- Lifetime value summary ----
        public decimal TotalPremiumCollected { get; set; }
        public decimal TotalOutstandingBalance { get; set; }
        public int ActivePolicyCount { get; set; }
        public int TotalPolicyCount { get; set; }
    }

    public class ClientPhoneItem
    {
        public string PhoneNumber { get; set; } = string.Empty;
        public string PhoneType { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public bool IsWhatsApp { get; set; }
    }

    public class ClientAddressItem
    {
        public string AddressType { get; set; } = string.Empty;
        public string AddressLine1 { get; set; } = string.Empty;
        public string? AddressLine2 { get; set; }
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Postcode { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
    }

    public class PolicyTimelineItem
    {
        public int PolicyId { get; set; }
        public string CoverNoteNumber { get; set; } = string.Empty;
        public string? RegistrationNumber { get; set; }
        public string PolicyClassName { get; set; } = string.Empty;
        public string InsurerName { get; set; } = string.Empty;
        public string PolicyStatus { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public decimal NetPremiumPayable { get; set; }
        public bool IsRenewal { get; set; }
    }

    public class PaymentHistoryItem
    {
        public DateTime PaymentDate { get; set; }
        public string ReceiptNumber { get; set; } = string.Empty;
        public string CoverNoteNumber { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal AmountPaid { get; set; }
        public bool IsClearedAndSettled { get; set; }
    }

    public class ClaimHistoryItem
    {
        public string ClaimReferenceNumber { get; set; } = string.Empty;
        public string CoverNoteNumber { get; set; } = string.Empty;
        public DateTime ClaimDate { get; set; }
        public string ClaimType { get; set; } = string.Empty;
        public string ClaimStatus { get; set; } = string.Empty;
        public decimal? ApprovedClaimAmount { get; set; }
        public decimal? EstimatedLossAmount { get; set; }
    }

    public class RenewalNoticeItem
    {
        public string CoverNoteNumber { get; set; } = string.Empty;
        public string NoticeType { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public string Channel { get; set; } = string.Empty;
        public bool IsDelivered { get; set; }
    }
}