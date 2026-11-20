using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    // Only fields that the Admin Create/Edit forms are allowed to write.
    public class BrandFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Brand name is required.")]
        [StringLength(100, ErrorMessage = "Brand name cannot exceed 100 characters.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(120, ErrorMessage = "Slug cannot exceed 120 characters.")]
        public string? Slug { get; set; }

        public bool IsActive { get; set; } = true;

        // Read-only display state, not copied into the EF entity from a POST.
        public string? LogoUrl { get; set; }

        public IFormFile? LogoFile { get; set; }

        public bool RemoveLogo { get; set; }
    }

    public class BrandListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public bool IsActive { get; set; }
        public int ProductCount { get; set; }
    }

    public class BrandIndexViewModel
    {
        public string? Search { get; set; }
        public string Status { get; set; } = "all";
        public int TotalBrands { get; set; }
        public int ActiveBrands { get; set; }
        public int ProductBrandCount { get; set; }
        public List<BrandListItemViewModel> Brands { get; set; } = new();
    }

    public class BrandDeleteViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public int ProductCount { get; set; }
    }
}
