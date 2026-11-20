using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Customers;
using Jersey_ecommerce.Services;
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
        var deliveredOrderId = userId == null ? (int?)null :
            await db.OrderItems.AsNoTracking()
                .Where(x => x.ProductVariant != null &&
                    x.ProductVariant.ProductId == product.Id &&
                    x.Order.UserId == userId && x.Order.Status == "Delivered")
                .OrderByDescending(x => x.Order.CreatedAt)
                .Select(x => (int?)x.OrderId).FirstOrDefaultAsync();
        var ownReview = userId == null ? null :
            await db.Reviews.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ProductId == product.Id);
        ViewBag.DeliveredOrderId = deliveredOrderId;
        ViewBag.OwnReviewStatus = ownReview == null ? "NotReviewed" :
            ownReview.IsApproved && !ownReview.IsRejected ? "Approved" :
            ownReview.IsRejected ? "Rejected" : "Pending";
        ViewBag.CanReview = deliveredOrderId.HasValue &&
            (ownReview == null || ownReview.IsRejected);
        return View(product);
    }

    [HttpPost("Review/{productId:int}"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int productId, int rating, string? title, string comment)
    {
        var slug = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId && p.IsPublished)
            .Select(p => p.Slug).SingleOrDefaultAsync();
        if (slug == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await CustomerReviewService.SubmitAsync(
            db, userId, productId, rating, title, comment);
        TempData["ReviewMessage"] = result.Message;
        return RedirectToAction(nameof(Details), new { slug });
    }
}
