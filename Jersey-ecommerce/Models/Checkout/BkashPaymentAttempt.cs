using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Checkout;

public sealed class BkashPaymentAttempt
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    [Required, MaxLength(64)]
    public string Nonce { get; set; } = "";
    [MaxLength(128)]
    public string? GatewayPaymentId { get; set; }
    [MaxLength(2048)]
    public string? CheckoutUrl { get; set; }
    [Required, MaxLength(24)]
    public string Status { get; set; } = "Creating";
    [MaxLength(128)]
    public string? GatewayTransactionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
