using Jersey_ecommerce.Models.Catalog;
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Shopping
{
    public class WishListItem
    {
        public int Id { get; set; }

        [Required]
        public int WishlistId { get; set; }

        [Required]
        public int ProductId { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public WishList Wishlist { get; set; } = null!;

        public Product Product { get; set; } = null!;
    }
}
