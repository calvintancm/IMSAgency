using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Claims;

namespace ImsAgency.Web.Models.IMS
{
    // THE CENTRAL TABLE of IMS Agency. Every policy — motor or non-motor —
    // is a row here. VehicleId is nullable because non-motor policies
    // (FWHS, PA, Medical, Travel, Home, Fire) have no vehicle.
    public class Policy
    {
        [Key]
        public int PolicyId { get; set; }

        // The agency's OWN reference number, generated immediately when
        // a policy is created (even before the insurer issues a policy number)
        [Required]
        [StringLength(50)]
        public string CoverNoteNumber { get; set; } = string.Empty; // "CN-2024-10001"

        // The INSURER's policy number — only filled in once the insurer
        // actually issues the policy. Left blank for "Draft"/"Quoted" status.
        [StringLength(50)]
        public string? PolicyNumber { get; set; }

        [ForeignKey(nameof(Client))]
        public int ClientId { get; set; }

        // NULL for non-motor policies
        [ForeignKey(nameof(Vehicle))]
        public int? VehicleId { get; set; }

        [ForeignKey(nameof(Insurer))]
        public int InsurerId { get; set; }

        // FK -> PolicyClasses.ClassCode (string alternate key, NOT the int PK)
        [Required]
        [StringLength(20)]
        public string PolicyClassCode { get; set; } = string.Empty;

        // "Draft", "Quoted", "Active", "Expired", "Lapsed", "Cancelled"
        [Required]
        [StringLength(30)]
        public string PolicyStatus { get; set; } = "Draft";

        [Required]
        public DateTime StartDate { get; set; }

        // KEY field for the Renewal Pipeline (Feature 1) — everything in
        // RenewalController is built around comparing this to DateTime.UtcNow
        [Required]
        public DateTime ExpiryDate { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal NcdPercentage { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal SumInsured { get; set; }

        [StringLength(20)]
        public string? AgentCode { get; set; }

        // True if this policy was created via the "Renew Policy" clone
        // button (Feature 4) rather than as a brand-new policy
        public bool IsRenewal { get; set; } = false;

        // Self-referencing FK -> the prior year's Policy this one renews from.
        // Configured with DeleteBehavior.Restrict in OnModelCreating.
        public int? PreviousPolicyId { get; set; }

        // Last time a renewal reminder (WhatsApp/SMS/Email) was sent for this policy
        public DateTime? RenewalReminderSentAt { get; set; }

        // How many reminders have been sent (30/60/90 day cycle) —
        // used to avoid spamming the client with duplicate reminders
        public int RenewalReminderCount { get; set; } = 0;

        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // ---- Navigation ----
        public Client? Client { get; set; }
        public Vehicle? Vehicle { get; set; }
        public Insurer? Insurer { get; set; }
        public PolicyClass? PolicyClass { get; set; }

        [StringLength(256)]
        public string? CreatedBy { get; set; }

        // Username of the agent/staff who last edited this policy record
        [StringLength(256)]
        public string? UpdatedBy { get; set; }

        // Concurrency token — EF Core auto-populates and checks this on
        // SaveChanges(). If two agents edit the same policy at the same
        // time, the second save throws DbUpdateConcurrencyException
        // instead of silently overwriting the first agent's changes.
        [Timestamp]
        public byte[]? RowVersion { get; set; }


        // ---- Add this navigation property near the other navigations ----

        // Links to the agent via AgentCode (alternate key relationship,
        // configured in OnModelCreating). Nullable because AgentCode
        // itself is nullable on Policy.
        public AgentProfile? Agent { get; set; }

        // 1:1 — every policy has exactly one premium breakdown
        public PremiumLedger? PremiumLedger { get; set; }

        // 1:1 (optional) — only present once invoiced to LHDN
        public LhdnEInvoiceRecord? LhdnEInvoiceRecord { get; set; }

        // 1:many — group policies (FWHS, PA Group, Medical Group) have employees
        public ICollection<PolicyGroupEmployee> PolicyGroupEmployees { get; set; } = new List<PolicyGroupEmployee>();

        // 1:many — renewal reminder history for this policy
        public ICollection<RenewalNotice> RenewalNotices { get; set; } = new List<RenewalNotice>();

        // 1:many — claims filed against this policy
        public ICollection<Claim> Claims { get; set; } = new List<Claim>();

        public ICollection<CustomerPaymentLedger> CustomerPaymentLedgers { get; set; }
       = new List<CustomerPaymentLedger>();
    }
}