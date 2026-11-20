using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jersey_ecommerce.Models.Catalog
{
    public class Product
    {
        public int Id { get; set; }
        [Required, StringLength(200)]
        public string Name { get; set; } = string.Empty;
        [Required, StringLength(220)]
        public string Slug { get; set; } = string.Empty;
        [StringLength(1000)]
        public string? ShortDescription { get; set; }
        public string? Description { get; set; }
        public int CategoryId { get; set; }
        public int StockQuantity { get; set; }
        public int? BrandId { get; set; }
        [StringLength(100)]
        public string? SKU { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal BasePrice { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal? CompareAtPrice { get; set; }
        public bool IsFeatured { get; set; } = false;
        public bool IsNewArrival { get; set; } = false;
        public bool IsPublished { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Category Category { get; set; } = null!;
        public Brand? Brand { get; set; }
        public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
        public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
        public ICollection<ProductCollection> ProductCollections { get; set; } = new List<ProductCollection>();

        // NOT a database column: stock lives in ProductVariant.StockQuantity.
        // Load Variants when calculating this property, e.g. .Include(p => p.Variants).
        [NotMapped]
        public long AvailableQuantity => Variants.Where(v => v.IsActive)
            .Sum(v => (long)v.StockQuantity);
        [NotMapped]
        public bool IsInStock => AvailableQuantity > 0;
    }
}
