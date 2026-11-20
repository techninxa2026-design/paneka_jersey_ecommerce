
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Promotions
{
    public class Coupon
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Description { get; set; }

        // Percentage or Fixed
        [Required]
        [StringLength(20)]
        public string DiscountType { get; set; } = "Percentage";

        [Range(typeof(decimal), "0.01", "1000000")]
        public decimal DiscountValue { get; set; }

        [Range(typeof(decimal), "0", "100000000")]
        public decimal MinimumOrderAmount { get; set; }

        [Range(typeof(decimal), "0", "100000000")]
        public decimal? MaximumDiscountAmount { get; set; }

        public int? UsageLimit { get; set; }

        public int? UsageLimitPerUser { get; set; }

        public int UsageCount { get; set; }

        public DateTime? StartsAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<CouponUsage> Usages { get; set; }
            = new List<CouponUsage>();
    }
}
