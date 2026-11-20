using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Models.Promotions;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public sealed class CouponFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Enter a coupon code.")]
        [StringLength(50, MinimumLength = 3)]
        [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9_-]*$",
            ErrorMessage = "Use letters, numbers, hyphens, or underscores only.")]
        public string Code { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        public string DiscountType { get; set; } = "Percentage";

        [Range(typeof(decimal), "0.01", "99999999", ErrorMessage = "Enter a discount greater than zero.")]
        public decimal DiscountValue { get; set; }

        [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Minimum order amount cannot be negative.")]
        public decimal MinimumOrderAmount { get; set; }

        [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Maximum discount must be greater than zero.")]
        public decimal? MaximumDiscountAmount { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Enter a positive usage limit.")]
        public int? UsageLimit { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Enter a positive per-user limit.")]
        public int? UsageLimitPerUser { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime? StartsAt { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime? ExpiresAt { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public sealed class CouponRowViewModel
    {
        public Coupon Coupon { get; set; } = null!;
        public string Status { get; set; } = "active";
    }

    public sealed class CouponsIndexViewModel
    {
        public string? Search { get; set; }
        public string Status { get; set; } = "all";
        public List<CouponRowViewModel> Coupons { get; set; } = new();
        public int Total { get; set; }
        public int Active { get; set; }
        public int Scheduled { get; set; }
        public int Expired { get; set; }
        public int Exhausted { get; set; }
        public int Inactive { get; set; }
        public int TotalRedemptions { get; set; }
    }

    public sealed class CouponUsageViewModel
    {
        public Coupon Coupon { get; set; } = null!;
        public List<CouponUsage> Usages { get; set; } = new();
    }
}
