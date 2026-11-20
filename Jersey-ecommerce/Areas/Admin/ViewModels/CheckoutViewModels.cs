using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Services;

namespace Jersey_ecommerce.ViewModels.Checkout;

public sealed class CartLineDto
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public int VariantId { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Category { get; init; } = "";
    public string Size { get; init; } = "";
    public string Color { get; init; } = "";
    public string? ImageUrl { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public int StockQuantity { get; init; }
    public bool IsNewArrival { get; init; }
}

public sealed class CartSummaryDto
{
    public List<CartLineDto> Items { get; init; } = [];
    public int ItemCount => Items.Sum(i => i.Quantity);
    public decimal Subtotal => Items.Sum(i => i.Price * i.Quantity);
    public decimal DiscountAmount { get; init; }
    public string? PromoCode { get; init; }
    public decimal Total => Math.Max(0m, Subtotal - DiscountAmount);
}

public sealed class DeliveryInput
{
    [Required, StringLength(150, MinimumLength = 2)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(255)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^[+0-9\s-]{8,20}$", ErrorMessage = "Enter a valid contact number.")]
    public string Phone { get; set; } = "";
    [Required, StringLength(255)] public string AddressLine1 { get; set; } = "";
    [StringLength(255)] public string? AddressLine2 { get; set; }
    [Required, StringLength(100)] public string City { get; set; } = "";
    [Required, StringLength(100), BangladeshDistrict] public string District { get; set; } = "";
    [StringLength(20)] public string? PostalCode { get; set; }
    [Required, RegularExpression("^(Dhaka|OutsideDhaka)$")]
    public string Region { get; set; } = "Dhaka";
    [StringLength(500)] public string? Instructions { get; set; }
}

public sealed class DeliveryPageViewModel
{
    public DeliveryInput Delivery { get; set; } = new();
    public CartSummaryDto Cart { get; set; } = new();
    public decimal DhakaFee { get; init; }
    public decimal OutsideDhakaFee { get; init; }
}

public sealed class PaymentPageViewModel
{
    public DeliveryInput Delivery { get; set; } = new();
    public CartSummaryDto Cart { get; set; } = new();
    public decimal ShippingCost { get; init; }
    public bool AllowCashOnDelivery { get; init; } = true;
    // Manual bKash receipt reporting must remain available even when the
    // merchant wallet has not yet been entered in admin settings.
    public bool AllowManualBkash => true;
    public bool HasConfiguredBkashRecipient => !string.IsNullOrWhiteSpace(ManualBkashNumber);
    public string? ManualBkashNumber { get; init; }
    public string ManualBkashInstructions { get; init; } =
        "Send Money to the receiving number displayed, then submit your transaction details for staff verification.";
    public decimal GrandTotal => Cart.Total + ShippingCost;
    public string SubmissionToken { get; init; } = Guid.NewGuid().ToString("N");
    public string ManualInstructions { get; init; } = "Contact the store for advance-payment details. Your order stays unpaid until staff verify payment.";
}

public sealed class OrderSuccessViewModel
{
    public string OrderNumber { get; init; } = "";
    public decimal Total { get; init; }
    public string PaymentMethod { get; init; } = "";
    public string PaymentStatus { get; init; } = "Pending";
    public string ManualInstructions { get; init; } = "";
}
