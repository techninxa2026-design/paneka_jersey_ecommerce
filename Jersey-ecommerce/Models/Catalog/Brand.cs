using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Catalog
{
    public class Brand
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string Slug { get; set; } = string.Empty;

        [StringLength(500)]
        public string? LogoUrl { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<Product> Products { get; set; }
            = new List<Product>();
    }
}
