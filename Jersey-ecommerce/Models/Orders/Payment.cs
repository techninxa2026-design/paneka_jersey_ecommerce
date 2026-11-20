
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

        public Order Order { get; set; } = null!;

        public ICollection<PaymentTransaction> Transactions { get; set; }
            = new List<PaymentTransaction>();
    }
}
