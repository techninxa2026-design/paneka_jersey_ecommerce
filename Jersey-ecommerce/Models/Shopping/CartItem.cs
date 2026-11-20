using Jersey_ecommerce.Models.Catalog;
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Shopping
{
    public class CartItem
    {
        public int Id { get; set; }

        [Required]
        public int ShoppingCartId { get; set; }

        [Required]
        public int ProductVariantId { get; set; }

        [Range(1, 100)]
        public int Quantity { get; set; } = 1;

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public ShoppingCart ShoppingCart { get; set; } = null!;

        public ProductVariant ProductVariant { get; set; } = null!;
    
}
}
