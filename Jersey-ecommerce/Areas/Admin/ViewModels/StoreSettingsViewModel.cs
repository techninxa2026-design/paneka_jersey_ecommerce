using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public sealed class StoreSettingsViewModel
    {
        [Required(ErrorMessage = "Please enter the store name.")]
        [StringLength(120, MinimumLength = 2)]
        [Display(Name = "Store name")]
        public string StoreName { get; set; } = "PANENKA";

        [StringLength(220)]
        public string? Tagline { get; set; }

        [Required]
        [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Use a three-letter uppercase currency code, e.g. BDT.")]
        [Display(Name = "Currency code")]
        public string CurrencyCode { get; set; } = "BDT";

        [Required]
        [StringLength(100)]
        public string Country { get; set; } = "Bangladesh";

        [StringLength(7)]
        [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Use a hex value such as #111111.")]
        public string AccentColor { get; set; } = "#111111";

        // Existing image URLs are display-only and always reloaded from the database.
        public string? LogoUrl { get; set; }
        public string? FaviconUrl { get; set; }
        [Display(Name = "Store logo")]
        public IFormFile? LogoFile { get; set; }
        [Display(Name = "Favicon")]
        public IFormFile? FaviconFile { get; set; }
        public bool RemoveLogo { get; set; }
        public bool RemoveFavicon { get; set; }

        [EmailAddress]
        [StringLength(200)]
        [Display(Name = "Support email")]
        public string? SupportEmail { get; set; }

        [StringLength(45)]
        public string? ContactPhone { get; set; }
        [StringLength(500)]
        public string? StoreAddress { get; set; }
        [StringLength(45)]
        public string? WhatsAppNumber { get; set; }

        [Range(typeof(decimal), "0", "99999999", ErrorMessage = "Enter a non-negative amount.")]
        [Display(Name = "Standard shipping fee")]
        public decimal StandardShippingFee { get; set; }

        [Range(typeof(decimal), "0", "99999999", ErrorMessage = "Enter a non-negative amount.")]
        [Display(Name = "Free shipping threshold")]
        public decimal FreeShippingThreshold { get; set; }

        // Preference keys only. Other website services must explicitly consume these values.
        public bool IsMaintenanceMode { get; set; }
        public bool AllowCashOnDelivery { get; set; } = true;

        [Display(Name = "Manual bKash receiving number")]
        [RegularExpression(@"^01[3-9][0-9]{8}$",
            ErrorMessage = "Enter an 11-digit Bangladesh mobile number (01XXXXXXXXX), or leave blank to disable manual bKash.")]
        public string? ManualBkashNumber { get; set; }

        [StringLength(500)]
        [Display(Name = "bKash payment instructions")]
        public string? ManualBkashInstructions { get; set; } =
            "Send Money to the receiving number shown, then submit your sender number, transaction ID and payment time.";
        public bool SendOrderNotifications { get; set; } = true;

        [Url]
        [StringLength(500)]
        public string? FacebookUrl { get; set; }
        [Url]
        [StringLength(500)]
        public string? InstagramUrl { get; set; }
        [Url]
        [StringLength(500)]
        public string? TikTokUrl { get; set; }
        [Url]
        [StringLength(500)]
        public string? YouTubeUrl { get; set; }
        [Url]
        [StringLength(500)]
        public string? XUrl { get; set; }

        [StringLength(160)]
        public string? SeoTitle { get; set; }
        [StringLength(500)]
        public string? SeoDescription { get; set; }
        [StringLength(500)]
        public string? SeoKeywords { get; set; }
    }
}
