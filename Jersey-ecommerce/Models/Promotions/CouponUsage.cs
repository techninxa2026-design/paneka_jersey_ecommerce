
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Models.Orders;

namespace Jersey_ecommerce.Models.Promotions
{
    public class CouponUsage
    {
        public int Id { get; set; }

        [Required]
        public int CouponId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int OrderId { get; set; }

        [Range(typeof(decimal), "0", "100000000")]
        public decimal DiscountAmount { get; set; }

        public DateTime UsedAt { get; set; } = DateTime.UtcNow;

        public Coupon Coupon { get; set; } = null!;

        public ApplicationUser User { get; set; } = null!;

        public Order Order { get; set; } = null!;
    }
}
