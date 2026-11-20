using Jersey_ecommerce.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers.Api;

// Drives the existing PANENKA homepage from the admin-managed catalog and banners.
[ApiController, AllowAnonymous, Route("api/storefront/home")]
public sealed class HomeStorefrontApiController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsPublished)
            .Include(p => p.Category).Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .OrderByDescending(p => p.IsNewArrival).ThenByDescending(p => p.CreatedAt)
            .Take(24).AsSplitQuery().ToListAsync(ct);
        var slides = await db.HeroSlides.AsNoTracking().Where(s => s.IsActive &&
            (!s.StartsAt.HasValue || s.StartsAt.Value <= now) &&
            (!s.EndsAt.HasValue || s.EndsAt.Value >= now))
            .OrderBy(s => s.DisplayOrder).ThenByDescending(s => s.Id)
            .Select(s => new { s.Title, s.Subtitle, s.ImageUrl, s.MobileImageUrl, s.ButtonText, s.ButtonUrl })
            .ToListAsync(ct);
        var banners = await db.Banners.AsNoTracking().Where(b => b.IsActive &&
            (b.Placement == "Home" || b.Placement == "home" ||
                b.Placement == "HomeTop" || b.Placement == "HomeMiddle" || b.Placement == "Footer") &&
            (!b.StartsAt.HasValue || b.StartsAt.Value <= now) &&
            (!b.EndsAt.HasValue || b.EndsAt.Value >= now))
            .OrderBy(b => b.DisplayOrder)
            .Select(b => new { b.Title, b.Subtitle, b.ImageUrl, b.LinkUrl, b.Placement })
            .ToListAsync(ct);
        var collections = await db.Collections.AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).Select(c => new { c.Id, c.Name, c.Slug, c.ImageUrl })
            .ToListAsync(ct);
        var settings = await db.SiteSettings.AsNoTracking()
            .Where(x => x.Key == "social.instagram" || x.Key == "social.facebook" ||
                x.Key == "social.youtube" || x.Key == "social.tiktok" ||
                x.Key == "seo.title" || x.Key == "seo.description" || x.Key == "general.tagline")
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var response = products.Select(p => new
        {
            id = p.Id, p.Name, p.Slug, price = p.BasePrice, p.CompareAtPrice,
            category = p.Category?.Name ?? "Football Jersey", p.IsNewArrival, p.IsFeatured,
            p.ShortDescription, description = p.ShortDescription ?? p.Description,
            stockQuantity = p.Variants.Where(v => v.IsActive && v.Size.IsActive && v.Color.IsActive).Sum(v => (long)v.StockQuantity),
            imageUrl = p.Images.Where(i => !(i.AltText ?? "").StartsWith("variant:"))
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                .Select(i => i.ImageUrl).FirstOrDefault(),
            url = "/Product/Details/" + Uri.EscapeDataString(p.Slug)
        });
        return Ok(new { products = response, slides, banners, collections, settings });
    }
}
