using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Checkout;

// Single checkout input DTO shared by CheckoutController and CheckoutService.
public sealed class PlaceOrderInput
{
    [Required, RegularExpression("^[a-fA-F0-9]{32}$")]
    public string SubmissionToken { get; set; } = "";

    [Required, RegularExpression("^(COD|Bkash)$")]
    public string PaymentMethod { get; set; } = "";

    // Required only when PaymentMethod == Bkash; verified in the checkout service.
    // Do not accept PIN/OTP anywhere in this flow.
    [StringLength(20)]
    public string? BkashSenderNumber { get; set; }

    [StringLength(40)]
    public string? BkashTransactionId { get; set; }

    public DateTime? BkashPaidAtLocal { get; set; }

    // Prevent checkout from silently using a different merchant number while
    // the customer had the popup open and was sending money.
    [StringLength(20)]
    public string? BkashRecipientNumber { get; set; }
}
