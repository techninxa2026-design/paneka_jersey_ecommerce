
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Areas.Identity;

namespace Jersey_ecommerce.Models.Orders
{
    public class Order
    {
        public int Id { get; set; }

        [Required]
        [StringLength(30)]
        public string OrderNumber { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        [Required]
        [StringLength(150)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [StringLength(256)]
        [EmailAddress]
        public string CustomerEmail { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string CustomerPhone { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string ShippingAddressLine1 { get; set; } = string.Empty;

        [StringLength(200)]
        public string? ShippingAddressLine2 { get; set; }

        [Required]
        [StringLength(100)]
        public string ShippingCity { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ShippingDistrict { get; set; }

        [StringLength(20)]
        public string? ShippingPostalCode { get; set; }

        [Required]
        [StringLength(100)]
        public string ShippingCountry { get; set; } = "Bangladesh";

        [Range(0, double.MaxValue)]
        public decimal Subtotal { get; set; }

        [Range(0, double.MaxValue)]
        public decimal DiscountAmount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal ShippingCost { get; set; }

        [Range(0, double.MaxValue)]
        public decimal TaxAmount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal TotalAmount { get; set; }

        [StringLength(50)]
        public string? CouponCode { get; set; }

        [StringLength(1000)]
        public string? CustomerNote { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public DateTime? PaidAt { get; set; }

        public DateTime? CancelledAt { get; set; }

        public ApplicationUser User { get; set; } = null!;

        public ICollection<OrderItem> Items { get; set; }
            = new List<OrderItem>();

        public Payment? Payment { get; set; }

        public Shipment? Shipment { get; set; }

        public ICollection<OrderStatusHistory> StatusHistory { get; set; }
            = new List<OrderStatusHistory>();
    }
}
