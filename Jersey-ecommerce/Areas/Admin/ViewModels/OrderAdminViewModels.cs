using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Models.Orders;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public sealed class OrderListRowViewModel
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? PaymentStatus { get; set; }
        public string? ShipmentStatus { get; set; }
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class OrdersIndexViewModel
    {
        public List<OrderListRowViewModel> Orders { get; set; } = new();
        public List<string> StatusOptions { get; set; } = new();
        public string? Search { get; set; }
        public string? Status { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public int FilteredCount { get; set; }
        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int SubmittedBkashCount { get; set; }
        public int ProcessingCount { get; set; }
        public int ShippedCount { get; set; }
        public int DeliveredCount { get; set; }
        public decimal RecordedOrderValue { get; set; }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)FilteredCount / PageSize));
    }

    public sealed class OrderDetailsViewModel
    {
        public Order Order { get; set; } = null!;
        public string? NextStatus { get; set; }
        public string? NextStatusHint { get; set; }
    }

    public sealed class OrderStatusUpdateViewModel
    {
        [Required]
        public int OrderId { get; set; }

        [Required]
        public string ExpectedStatus { get; set; } = string.Empty;

        [Required]
        public string NewStatus { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Note { get; set; }
    }
}
