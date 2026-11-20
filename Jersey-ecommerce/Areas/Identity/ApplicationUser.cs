using Jersey_ecommerce.Models.Customers;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Models.Shopping;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Areas.Identity
{
    public class ApplicationUser : IdentityUser
    {
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;

        // Customer addresses
        public ICollection<Address> Addresses { get; set; }
            = new List<Address>();

        // Customer orders
        public ICollection<Order> Orders { get; set; }
            = new List<Order>();

        // Shopping cart (one cart per user)
        public ShoppingCart? ShoppingCart { get; set; }

        // Wishlist (one wishlist per user)
        public WishList? Wishlist { get; set; }

        // Product reviews
        public ICollection<Review> Reviews { get; set; }
            = new List<Review>();
        [StringLength(500)]
        public string? ProfileImageUrl { get; set; }
    }
}
