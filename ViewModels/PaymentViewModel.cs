using System.ComponentModel.DataAnnotations;

namespace ImsAgency.Web.Models.ViewModels
{
    public class PaymentListViewModel
    {
        public int TotalPayments { get; set; }
        public decimal TotalAmountCollected { get; set; }
        public int PendingClearance { get; set; }
        public decimal OutstandingBalance { get; set; }
    }

    public class RecordPaymentViewModel
    {
        public int PaymentId { get; set; }

        [Required(ErrorMessage = "Please select a client")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a client")]
        public int ClientId { get; set; }
        public string? ClientDisplay { get; set; }

        [Required(ErrorMessage = "Please select a policy")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a policy")]
        public int PolicyId { get; set; }
        public string? PolicyDisplay { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow.Date;

        [Required(ErrorMessage = "Payment Method is required")]
        public string PaymentMethod { get; set; } = "Cash";

        [Required]
        [Range(0.01, 10000000, ErrorMessage = "Amount must be greater than 0")]
        public decimal AmountPaid { get; set; }

        [StringLength(100)]
        public string? BankOrIssueName { get; set; }

        [StringLength(100)]
        public string? ReferenceNumber { get; set; }

        public bool IsClearedAndSettled { get; set; } = true;

        public string? Remarks { get; set; }

        // Read-only display — filled from policy lookup
        public decimal? NetPremiumPayable { get; set; }
        public decimal? TotalPaidSoFar { get; set; }
        public decimal? OutstandingBalance { get; set; }
    }
}