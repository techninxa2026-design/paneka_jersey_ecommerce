
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Models.Catalog;

namespace Jersey_ecommerce.Models.Orders
{
    public class InventoryTransaction
    {
        public int Id { get; set; }

        [Required]
        public int ProductVariantId { get; set; }

        public int? OrderId { get; set; }

        [Required]
        [StringLength(40)]
        public string Type { get; set; } = string.Empty;

        // Positive for stock received; negative for stock removed.
        public int QuantityChange { get; set; }

        public int StockBefore { get; set; }

        public int StockAfter { get; set; }

        [StringLength(500)]
        public string? Note { get; set; }

        [StringLength(100)]
        public string? Reference { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ProductVariant ProductVariant { get; set; } = null!;

        public Order? Order { get; set; }
    }
}
