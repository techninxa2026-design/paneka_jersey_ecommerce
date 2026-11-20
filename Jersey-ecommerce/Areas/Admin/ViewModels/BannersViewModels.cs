using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Models.Content;
using Microsoft.AspNetCore.Http;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public sealed class BannersIndexViewModel
    {
        public List<HeroSlide> Slides { get; set; } = new();
        public List<Banner> Banners { get; set; } = new();
    }

    public sealed class HeroSlideFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Enter a slide title.")]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Subtitle { get; set; }

        [Display(Name = "Desktop image")]
        public IFormFile? ImageFile { get; set; }

        [Display(Name = "Mobile image")]
        public IFormFile? MobileImageFile { get; set; }

        public string? CurrentImageUrl { get; set; }
        public string? CurrentMobileImageUrl { get; set; }

        public bool RemoveMobileImage { get; set; }

        [StringLength(100)]
        [Display(Name = "Button text")]
        public string? ButtonText { get; set; }

        [StringLength(1000)]
        [Display(Name = "Button URL")]
        public string? ButtonUrl { get; set; }

        [Range(0, 99999)]
        [Display(Name = "Display order")]
        public int DisplayOrder { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Start (Bangladesh time)")]
        public DateTime? StartsAt { get; set; }

        [Display(Name = "End (Bangladesh time)")]
        public DateTime? EndsAt { get; set; }
    }

    public sealed class BannerFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Enter a banner title.")]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Subtitle { get; set; }

        [Display(Name = "Banner image")]
        public IFormFile? ImageFile { get; set; }

        public string? CurrentImageUrl { get; set; }

        [StringLength(1000)]
        [Display(Name = "Link URL")]
        public string? LinkUrl { get; set; }

        [Required]
        [StringLength(60)]
        public string Placement { get; set; } = "Home";

        [Range(0, 99999)]
        [Display(Name = "Display order")]
        public int DisplayOrder { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Start (Bangladesh time)")]
        public DateTime? StartsAt { get; set; }

        [Display(Name = "End (Bangladesh time)")]
        public DateTime? EndsAt { get; set; }
    }
}
