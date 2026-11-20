using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Catalog
{
    public class Color
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [StringLength(7)]
        [RegularExpression(
            @"^#([0-9a-fA-F]{6})$",
            ErrorMessage = "HexCode must be a valid six-digit hex color.")]
        public string? HexCode { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<ProductVariant> Variants { get; set; }
            = new List<ProductVariant>();
    }
}
