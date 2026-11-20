using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Models.Catalog;

// One OPTIONAL image for each size/color combination.
// Does not change the existing ProductVariant entity or ProductImage gallery.
[Index(nameof(ProductVariantId), IsUnique = true)]
public class ProductVariantImage
{
    public int Id { get; set; }

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    [Required, MaxLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
