
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Orders
{
    public class PaymentTransaction
    {
        public int Id { get; set; }

        [Required]
        public int PaymentId { get; set; }

        [Required]
        [StringLength(40)]
        public string TransactionType { get; set; } = "Payment";

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        [StringLength(200)]
        public string? ProviderTransactionId { get; set; }

        [StringLength(200)]
        public string? Reference { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        [StringLength(1000)]
        public string? FailureReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        public Payment Payment { get; set; } = null!;
    }
}
