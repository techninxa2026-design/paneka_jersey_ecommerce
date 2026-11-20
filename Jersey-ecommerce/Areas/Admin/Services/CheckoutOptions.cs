namespace Jersey_ecommerce.Services.Checkout;

public sealed class CheckoutOptions
{
    // If not specified, both regions use the Admin > Settings > Standard shipping fee.
    public decimal? DhakaShippingFee { get; set; }
    public decimal? OutsideDhakaShippingFee { get; set; }
    public string ManualPaymentInstructions { get; set; } =
        "Contact the store for advance-payment instructions. The team verifies payment before dispatch.";
}

public sealed class CheckoutException(string message) : Exception(message);
