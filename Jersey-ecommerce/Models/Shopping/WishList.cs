using Jersey_ecommerce.Areas.Identity;
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Shopping
{
    public class WishList
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser User { get; set; } = null!;

        public ICollection<WishListItem> Items { get; set; }
            = new List<WishListItem>();
    }
}
