using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Checkout;

// Single checkout input DTO shared by CheckoutController and CheckoutService.
public sealed class PlaceOrderInput
{
    [Required, RegularExpression("^[a-fA-F0-9]{32}$")]
    public string SubmissionToken { get; set; } = "";

    [Required, RegularExpression("^(COD|ManualAdvance|Bkash)$")]
    public string PaymentMethod { get; set; } = "";

    [StringLength(120)]
    public string? PaymentReference { get; set; }
}
