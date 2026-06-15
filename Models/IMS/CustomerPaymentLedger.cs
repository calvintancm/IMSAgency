using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImsAgency.Web.Models.IMS
{
    // Every premium payment received from a client is logged here.
    // PaymentController.RecordPayment writes to this table, and
    // FinancialReportController.PremiumCollection summarizes it.
    public class CustomerPaymentLedger
    {
        [Key]
        public int PaymentId { get; set; }

        [ForeignKey(nameof(Client))]
        public int ClientId { get; set; }

        [ForeignKey(nameof(Policy))]
        public int PolicyId { get; set; }

        [Required]
        public DateTime PaymentDate { get; set; }

        // "Cash", "Cheque", "FPX", "CreditCard", "DuitNowQR"
        [Required]
        [StringLength(30)]
        public string PaymentMethod { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountPaid { get; set; }

        // "Maybank", "CIMB", "Public Bank" — the bank for cheque/FPX payments
        [StringLength(100)]
        public string? BankOrIssueName { get; set; }

        // Cheque number / FPX reference / payment gateway transaction ID
        [StringLength(100)]
        public string? ReferenceNumber { get; set; }

        // True once the cheque has cleared / FPX confirmed settled
        public bool IsClearedAndSettled { get; set; } = false;

        // Auto-generated receipt reference, e.g. "RCT-2025-00012"
        // (generated the same way as ClientCode — year + sequence)
        [Required]
        [StringLength(50)]
        public string ReceiptNumber { get; set; } = string.Empty;

        public string? Remarks { get; set; }

        // Staff username who recorded this payment (e.g. from User.Identity.Name)
        [StringLength(100)]
        public string? RecordedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ---- Navigation ----
        public Client? Client { get; set; }
        //[ForeignKey("PolicyId")]
        public Policy? Policy { get; set; }
    }
}