using Jersey_ecommerce.Areas.Identity;
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Shopping
{
    public class ShoppingCart
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser User { get; set; } = null!;

        public ICollection<CartItem> Items { get; set; }
            = new List<CartItem>();
    }
}
