using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Jersey_ecommerce.Areas.Admin.ViewModels.Products;

public sealed class ProductIndexViewModel
{
    public string? Search { get; set; }
    public string Status { get; set; } = "all";
    public int? CategoryId { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int MatchingCount { get; set; }
    public int TotalProducts { get; set; }
    public int PublishedProducts { get; set; }
    public int DraftProducts { get; set; }
    public long TotalAvailableUnits { get; set; }
    public int OutOfStockProducts { get; set; }
    public List<SelectListItem> Categories { get; set; } = new();
    public List<ProductListRowViewModel> Products { get; set; } = new();
}

public sealed class ProductListRowViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? SKU { get; set; }
    public string CategoryName { get; set; } = "";
    public string? BrandName { get; set; }
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public long AvailableQuantity { get; set; }
    public int VariantCount { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ProductFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Enter a product name.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(220)]
    public string? Slug { get; set; }

    [StringLength(1000)]
    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a category.")]
    public int CategoryId { get; set; }

    public int? BrandId { get; set; }

    [StringLength(100)]
    public string? SKU { get; set; }

    [Range(typeof(decimal), "0", "9999999999", ErrorMessage = "Price cannot be negative.")]
    public decimal BasePrice { get; set; }

    [Range(typeof(decimal), "0", "9999999999", ErrorMessage = "Compare-at price cannot be negative.")]
    public decimal? CompareAtPrice { get; set; }

    public bool IsFeatured { get; set; }
    public bool IsNewArrival { get; set; }
    public bool IsPublished { get; set; }

    public List<IFormFile> NewImages { get; set; } = new();
    public List<int> RemoveImageIds { get; set; } = new();
    public int? PrimaryImageId { get; set; }
    public List<ProductPhotoViewModel> ExistingImages { get; set; } = new();
    public List<VariantInputViewModel> Variants { get; set; } = new();

    // Lookup data is repopulated on any failed POST.
    public List<SelectListItem> Categories { get; set; } = new();
    public List<SelectListItem> Brands { get; set; } = new();
    public List<LookupViewModel> Sizes { get; set; } = new();
    public List<LookupViewModel> Colors { get; set; } = new();
}

public sealed class LookupViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? HexCode { get; set; }
}

public sealed class ProductPhotoViewModel
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public bool IsPrimary { get; set; }
}

public sealed class VariantInputViewModel
{
    public int SizeId { get; set; }
    public int ColorId { get; set; }
    public string? SKU { get; set; }
    public int StockQuantity { get; set; }
    public decimal? Price { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ExistingImageUrl { get; set; }
    public IFormFile? Image { get; set; }
    public bool RemoveImage { get; set; }
}

public sealed class ProductDetailsViewModel
{
    public ProductListRowViewModel Product { get; set; } = new();
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public bool IsNewArrival { get; set; }
    public List<ProductPhotoViewModel> Photos { get; set; } = new();
    public List<VariantDetailsRowViewModel> Variants { get; set; } = new();
}

public sealed class VariantDetailsRowViewModel
{
    public string Size { get; set; } = "";
    public string Color { get; set; } = "";
    public string SKU { get; set; } = "";
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public string? ImageUrl { get; set; }
}
