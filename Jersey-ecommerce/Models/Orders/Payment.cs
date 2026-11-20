
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Orders
{
    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        [StringLength(40)]
        public string Method { get; set; } = "CashOnDelivery";

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        [StringLength(150)]
        public string? Provider { get; set; }

        [StringLength(200)]
        public string? ProviderPaymentId { get; set; }

        [StringLength(200)]
        public string? TransactionReference { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        [StringLength(3)]
        public string Currency { get; set; } = "BDT";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? PaidAt { get; set; }

        public DateTime? FailedAt { get; set; }

        // Manual bKash reconciliation; these are customer-reported details,
        // not gateway-verified evidence. Times are stored in UTC.
        [StringLength(20)]
        public string? ManualSenderNumber { get; set; }

        [StringLength(20)]
        public string? ManualRecipientNumber { get; set; }

        public DateTime? ManualClaimedPaidAt { get; set; }

        public DateTime? ManualSubmittedAt { get; set; }

        public DateTime? ManualReviewedAt { get; set; }

        [StringLength(450)]
        public string? ManualReviewNote { get; set; }

        [StringLength(450)]
        public string? ManualReviewedByUserId { get; set; }

        public Order Order { get; set; } = null!;

        public ICollection<PaymentTransaction> Transactions { get; set; }
            = new List<PaymentTransaction>();
    }
}
