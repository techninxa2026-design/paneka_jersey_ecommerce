namespace Jersey_ecommerce.ViewModels.Orders;

/// <summary>
/// Customer-owned order item with its unique per-product review status.
/// No order/customer identity is trusted from a submitted form.
/// </summary>
public sealed class DeliveredProductReviewViewModel
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string ProductSlug { get; set; } = "";
    public bool ProductIsPublished { get; set; }
    public int? ReviewId { get; set; }
    public string ReviewStatus { get; set; } = "NotReviewed";
    public int Rating { get; set; } = 5;
    public string? Title { get; set; }
    public string? Comment { get; set; }
    public bool CanSubmit => ReviewStatus is "NotReviewed" or "Rejected";
}
