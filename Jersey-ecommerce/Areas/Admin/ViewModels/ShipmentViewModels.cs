using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Jersey_ecommerce.Areas.Admin.ViewModels;

public static class ShipmentStatuses
{
    public static readonly string[] Values =
    {
        "Pending", "Packed", "Shipped", "InTransit", "OutForDelivery",
        "Delivered", "Failed", "Returned", "Cancelled"
    };

    public static bool IsValid(string? value) =>
        value is not null && Values.Contains(value, StringComparer.Ordinal);

    // The storefront's order status is authoritative. Courier updates may
    // advance only through real delivery stages, never jump from pending to
    // delivered or re-open a closed shipment.
    public static IReadOnlyList<string> AllowedNext(string? current) => current switch
    {
        "Pending" => ["Packed"],
        "Packed" => ["Shipped"],
        "Shipped" => ["InTransit", "OutForDelivery", "Failed"],
        "InTransit" => ["OutForDelivery", "Failed"],
        "OutForDelivery" => ["Delivered", "Failed"],
        "Failed" => ["InTransit", "OutForDelivery", "Returned"],
        "Delivered" => ["Returned"],
        _ => []
    };

    public static bool CanTransition(string? current, string? next) =>
        IsValid(current) && IsValid(next) &&
        (string.Equals(current, next, StringComparison.Ordinal) ||
         AllowedNext(current).Contains(next!, StringComparer.Ordinal));

    public static string Label(string? value) => value switch
    {
        "InTransit" => "In transit",
        "OutForDelivery" => "Out for delivery",
        _ => value ?? "Unknown"
    };

    public static List<SelectListItem> SelectItems() =>
        Values.Select(v => new SelectListItem(Label(v), v)).ToList();
}

public sealed class ShipmentFormViewModel
{
    public int? Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Please select an order.")]
    public int OrderId { get; set; }

    [Required(ErrorMessage = "Please select a status.")]
    public string Status { get; set; } = "Pending";

    [StringLength(100)]
    [Display(Name = "Courier / Delivery company")]
    public string? CourierName { get; set; }

    [StringLength(200)]
    [Display(Name = "Tracking number")]
    public string? TrackingNumber { get; set; }

    [StringLength(500)]
    [Url(ErrorMessage = "Enter a valid tracking URL.")]
    [Display(Name = "Tracking URL")]
    public string? TrackingUrl { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Estimated delivery date")]
    public DateTime? EstimatedDeliveryAt { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    // View-only: populated by controller. These are not saved to Shipment.
    public string? OrderNumber { get; set; }
    public string? CustomerName { get; set; }
    public List<SelectListItem> AvailableOrders { get; set; } = new();
    public List<string> AllowedStatuses { get; set; } = new();
}

public sealed class ShipmentListRowViewModel
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? CourierName { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? EstimatedDeliveryAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

public sealed class ShipmentListViewModel
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public int AllCount { get; set; }
    public int PendingCount { get; set; }
    public int OnTheWayCount { get; set; }
    public int DeliveredCount { get; set; }
    public List<ShipmentListRowViewModel> Rows { get; set; } = new();
}
