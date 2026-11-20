using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers;

// Full product page, approved customer reviews, and product gallery from the existing schema.
[Route("Product")]
public sealed class ProductController(ApplicationDbContext db) : Controller
{
    [HttpGet("Details/{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> Details(string slug)
    {
        var product = await db.Products.AsNoTracking().AsSplitQuery()
            .Include(p => p.Category).Include(p => p.Brand)
            .Include(p => p.Images).Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .SingleOrDefaultAsync(p => p.IsPublished && p.Slug == slug);
        if (product == null && int.TryParse(slug, out var id))
            product = await db.Products.AsNoTracking().AsSplitQuery()
                .Include(p => p.Category).Include(p => p.Brand).Include(p => p.Images)
                .Include(p => p.Variants).ThenInclude(v => v.Size)
                .Include(p => p.Variants).ThenInclude(v => v.Color)
                .FirstOrDefaultAsync(p => p.IsPublished && p.Id == id);
        if (product == null) return NotFound();
        ViewBag.Reviews = await db.Reviews.AsNoTracking().Where(x => x.ProductId == product.Id && x.IsApproved && !x.IsRejected)
            .Include(x => x.User).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync();
        ViewBag.VariantImages = await db.ProductVariantImages.AsNoTracking()
            .Where(x => x.ProductVariant.ProductId == product.Id)
            .ToDictionaryAsync(x => x.ProductVariantId, x => x.ImageUrl);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        ViewBag.CanReview = userId != null &&
            await db.OrderItems.AnyAsync(x => x.ProductVariant!.ProductId == product.Id &&
                x.Order.UserId == userId && x.Order.Status == "Delivered") &&
            !await db.Reviews.AnyAsync(x => x.UserId == userId && x.ProductId == product.Id);
        return View(product);
    }

    [HttpPost("Review/{productId:int}"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int productId, int rating, string? title, string comment)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var product = await db.Products.AsNoTracking().Where(p => p.Id == productId && p.IsPublished)
            .Select(p => new { p.Slug }).SingleOrDefaultAsync();
        if (product == null) return NotFound();
        if (rating < 1 || rating > 5 || string.IsNullOrWhiteSpace(comment) || comment.Trim().Length > 2000 ||
            title?.Length > 180)
        {
            TempData["ReviewMessage"] = "Choose 1–5 stars and a comment within 2000 characters.";
            return RedirectToAction(nameof(Details), new { slug = product.Slug });
        }
        bool purchased = await db.OrderItems.AnyAsync(x => x.ProductVariant!.ProductId == productId &&
            x.Order.UserId == userId && x.Order.Status == "Delivered");
        if (!purchased || await db.Reviews.AnyAsync(x => x.UserId == userId && x.ProductId == productId))
        {
            TempData["ReviewMessage"] = "One review is allowed per customer after purchasing this product.";
            return RedirectToAction(nameof(Details), new { slug = product.Slug });
        }
        db.Reviews.Add(new Review
        {
            ProductId = productId, UserId = userId, Rating = rating, Title = title?.Trim(),
            Comment = comment.Trim(), IsApproved = false, IsRejected = false
        });
        try
        {
            await db.SaveChangesAsync();
            TempData["ReviewMessage"] = "Review submitted for moderation.";
        }
        catch (DbUpdateException)
        {
            // The unique (customer, product) index rejects parallel submissions.
            // Do not return an unhandled 500 after a browser double-click.
            TempData["ReviewMessage"] = "Your review was already submitted or could not be saved. Refresh the page.";
        }
        return RedirectToAction(nameof(Details), new { slug = product.Slug });
    }
}
