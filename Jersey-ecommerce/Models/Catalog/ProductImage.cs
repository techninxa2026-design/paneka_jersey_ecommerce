using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Catalog
{
    public class ProductImage
    {
        public int Id { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required]
        [StringLength(500)]
        public string ImageUrl { get; set; } = string.Empty;

        [StringLength(250)]
        public string? AltText { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsPrimary { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Product Product { get; set; } = null!;
    }
}
