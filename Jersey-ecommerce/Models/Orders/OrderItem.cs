
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Models.Catalog;

namespace Jersey_ecommerce.Models.Orders
{
    public class OrderItem
    {
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        public int? ProductVariantId { get; set; }

        [Required]
        [StringLength(200)]
        public string ProductName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? SKU { get; set; }

        [StringLength(100)]
        public string? SizeName { get; set; }

        [StringLength(100)]
        public string? ColorName { get; set; }

        [StringLength(500)]
        public string? ImageUrl { get; set; }

        [Range(1, 1000)]
        public int Quantity { get; set; } = 1;

        [Range(0, double.MaxValue)]
        public decimal UnitPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal DiscountAmount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal LineTotal { get; set; }

        public Order Order { get; set; } = null!;

        public ProductVariant? ProductVariant { get; set; }
    }
}
