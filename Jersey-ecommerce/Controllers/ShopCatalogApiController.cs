using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Jersey_ecommerce.Controllers;

/// <summary>
/// Public, paginated PANENKA shop catalog.
/// GET /api/storefront/shop?search=...&categoryId=...&sizeId=...&minPrice=...&maxPrice=...&sort=...&page=...
/// Only published products are returned. Category, stock and size filters run in SQL before paging.
/// Does not replace the existing StorefrontApiController for the homepage.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/storefront/shop")]
public sealed class ShopCatalogApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ShopCatalogApiController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        string? search = null,
        int? categoryId = null,
        int? sizeId = null,
        string? collection = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool inStock = false,
        string sort = "newest",
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 48);
        search = search?.Trim();
        collection = collection?.Trim();
        if (collection?.Length > 120) return BadRequest(new { message = "Invalid collection." });

        if (search?.Length > 120)
            return BadRequest(new { message = "Search is limited to 120 characters." });
        if (categoryId is <= 0 || sizeId is <= 0)
            return BadRequest(new { message = "Invalid category or size." });
        if (minPrice is < 0 || maxPrice is < 0 ||
            (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice))
            return BadRequest(new { message = "Invalid price range." });

        // The returned price is Product.BasePrice. Variant-specific prices are not substituted here.
        IQueryable<Product> query = _db.Products.AsNoTracking().Where(p => p.IsPublished);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.Name.Contains(search) || p.Slug.Contains(search) ||
                (p.SKU != null && p.SKU.Contains(search)) ||
                (p.Brand != null && p.Brand.Name.Contains(search)) ||
                p.Variants.Any(v => v.SKU != null && v.SKU.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(collection))
            query = query.Where(p => p.ProductCollections.Any(pc => pc.Collection.IsActive && pc.Collection.Slug == collection));

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        // A size is available only when an active variant has stock.
        if (sizeId.HasValue)
            query = query.Where(p => p.Variants.Any(v =>
                v.SizeId == sizeId.Value && v.IsActive && v.Size.IsActive && v.Color.IsActive && v.StockQuantity > 0));

        if (minPrice.HasValue)
            query = query.Where(p => p.BasePrice >= minPrice.Value);
        if (maxPrice.HasValue)
            query = query.Where(p => p.BasePrice <= maxPrice.Value);

        if (inStock)
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Size.IsActive && v.Color.IsActive && v.StockQuantity > 0));

        int total = await query.CountAsync(cancellationToken);
        int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);

        query = sort switch
        {
            "featured" => query.OrderByDescending(p => p.IsFeatured)
                               .ThenByDescending(p => p.CreatedAt)
                               .ThenByDescending(p => p.Id),
            "price_asc" => query.OrderBy(p => p.BasePrice).ThenBy(p => p.Id),
            "price_desc" => query.OrderByDescending(p => p.BasePrice).ThenBy(p => p.Id),
            "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
        };

        var products = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        // All categories and available sizes from published merchandise, regardless of current filters.
        var categories = await _db.Categories.AsNoTracking()
            .Where(c => _db.Products.Any(p => p.CategoryId == c.Id && p.IsPublished))
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                id = c.Id,
                name = c.Name,
                count = _db.Products.Count(p => p.CategoryId == c.Id && p.IsPublished)
            })
            .ToListAsync(cancellationToken);

        var sizes = await _db.Sizes.AsNoTracking()
            .Where(s => s.IsActive && _db.ProductVariants.Any(v =>
                v.SizeId == s.Id && v.IsActive && v.Color.IsActive && v.StockQuantity > 0 &&
                _db.Products.Any(p => p.Id == v.ProductId && p.IsPublished)))
            .OrderBy(s => s.Id)
            .Select(s => new { id = s.Id, name = s.Name })
            .ToListAsync(cancellationToken);

        var bounds = await _db.Products.AsNoTracking()
            .Where(p => p.IsPublished)
            .GroupBy(p => 1)
            .Select(group => new { min = group.Min(p => p.BasePrice), max = group.Max(p => p.BasePrice) })
            .FirstOrDefaultAsync(cancellationToken);

        var sizeNames = sizes.ToDictionary(s => s.id, s => s.name);

        var response = products.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            slug = p.Slug,
            categoryId = p.CategoryId,
            category = p.Category?.Name ?? "Football Jersey",
            price = p.BasePrice,
            compareAtPrice = p.CompareAtPrice,
            isNewArrival = p.IsNewArrival,
            isFeatured = p.IsFeatured,
            description = p.ShortDescription ?? p.Description,
            stockQuantity = p.Variants.Where(v => v.IsActive && v.Size.IsActive && v.Color.IsActive).Sum(v => (long)v.StockQuantity),
            availableSizes = p.Variants
                .Where(v => v.IsActive && v.Size.IsActive && v.Color.IsActive && v.StockQuantity > 0 && sizeNames.ContainsKey(v.SizeId))
                .Select(v => v.SizeId)
                .Distinct()
                .OrderBy(id => id)
                .Select(id => new { id, name = sizeNames[id] })
                .ToList(),
            imageUrl = MainImage(p.Images)
        }).ToList();

        return Ok(new
        {
            products = response,
            total,
            page,
            pageSize,
            totalPages,
            categories,
            sizes,
            priceBounds = new { min = bounds?.min ?? 0m, max = bounds?.max ?? 0m }
        });
    }

    [HttpGet("banner")]
    public async Task<IActionResult> ShopBanner(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var banner = await _db.Banners.AsNoTracking()
            .Where(x => x.IsActive && x.Placement == "Shop" &&
                (!x.StartsAt.HasValue || x.StartsAt.Value <= now) &&
                (!x.EndsAt.HasValue || x.EndsAt.Value >= now))
            .OrderBy(x => x.DisplayOrder).FirstOrDefaultAsync(ct);
        if (banner == null) return NoContent();
        return Ok(new { banner.Title, banner.Subtitle, banner.ImageUrl, banner.LinkUrl });
    }

    private static string? MainImage(IEnumerable<ProductImage> images)
    {
        return images
            .Where(img => !(ReadString(img, "AltText") ?? string.Empty)
                .StartsWith("variant:", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(img => ReadBool(img, "IsPrimary", "IsMain"))
            .ThenBy(img => ReadInt(img, "DisplayOrder", "SortOrder"))
            .Select(img => ReadString(img, "ImageUrl", "ImagePath", "FilePath", "Url", "Path"))
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    private static string? ReadString(object obj, params string[] names)
    {
        foreach (var name in names)
        {
            var property = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetValue(obj) is string value && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static bool ReadBool(object obj, params string[] names)
    {
        foreach (var name in names)
            if (obj.GetType().GetProperty(name)?.GetValue(obj) is bool value)
                return value;
        return false;
    }

    private static int ReadInt(object obj, params string[] names)
    {
        foreach (var name in names)
            if (obj.GetType().GetProperty(name)?.GetValue(obj) is int value)
                return value;
        return 0;
    }
}
