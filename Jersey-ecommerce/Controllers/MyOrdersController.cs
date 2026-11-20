using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Services.Checkout;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.ViewModels.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers;

[Authorize]
[Route("MyOrders")]
public sealed class MyOrdersController(ApplicationDbContext db, OrderCancellationService canceller) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1)
    {
        const int pageSize = 20;
        var query = db.Orders.AsNoTracking().Where(o => o.UserId == UserId);
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pages);

        var orders = await query.Include(o => o.Payment).Include(o => o.Shipment)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewData["Page"] = page;
        ViewData["TotalPages"] = pages;
        ViewData["TotalCount"] = total;
        return View(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var order = await db.Orders.AsNoTracking().AsSplitQuery()
            .Include(o => o.Payment).Include(o => o.Shipment)
            .Include(o => o.StatusHistory)
            .Include(o => o.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
            .SingleOrDefaultAsync(o => o.Id == id && o.UserId == UserId);
        if (order == null) return NotFound();

        // Show the review progress on the delivered order itself, rather than
        // leaving the buyer to discover a form at the bottom of a product page.
        if (order.Status == "Delivered")
        {
            var productIds = order.Items
                .Where(i => i.ProductVariant?.Product != null)
                .Select(i => i.ProductVariant!.ProductId)
                .Distinct().ToArray();
            var reviews = await db.Reviews.AsNoTracking()
                .Where(x => x.UserId == UserId && productIds.Contains(x.ProductId))
                .ToListAsync();
            ViewBag.CustomerProductReviews = reviews.ToDictionary(x => x.ProductId,
                x => x.IsApproved && !x.IsRejected ? "Approved"
                    : x.IsRejected ? "Rejected" : "Pending");
        }

        return View(order);
    }

    [HttpGet("{orderId:int}/Review/{productId:int}")]
    public async Task<IActionResult> Review(int orderId, int productId)
    {
        var model = await GetReviewModelAsync(orderId, productId);
        if (model == null) return NotFound();

        if (TempData["ReviewResult"] is string message)
            ViewBag.ReviewResult = message;

        return View(model);
    }

    [HttpPost("{orderId:int}/Review/{productId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        int orderId, int productId, int rating, string? title, string? comment)
    {
        // Confirm buyer and delivered order before saving anything. An item in
        // someone else's order (or an undelivered parcel) cannot be reviewed.
        var model = await GetReviewModelAsync(orderId, productId);
        if (model == null) return NotFound();

        var result = await CustomerReviewService.SubmitAsync(db, UserId,
            productId, rating, title, comment);
        TempData["ReviewResult"] = result.Message;
        return RedirectToAction(nameof(Review), new { orderId, productId });
    }

    private async Task<DeliveredProductReviewViewModel?> GetReviewModelAsync(
        int orderId, int productId)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId && o.UserId == UserId &&
                        o.Status == "Delivered")
            .Select(o => new { o.Id, o.OrderNumber })
            .SingleOrDefaultAsync();
        if (order == null) return null;

        var item = await db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId &&
                i.ProductVariant != null && i.ProductVariant.ProductId == productId)
            .Select(i => new
            {
                i.ProductName,
                i.ImageUrl,
                ProductSlug = i.ProductVariant!.Product.Slug
            })
            .FirstOrDefaultAsync();
        if (item == null) return null;

        var existing = await db.Reviews.AsNoTracking()
            .SingleOrDefaultAsync(r => r.ProductId == productId && r.UserId == UserId);

        return new DeliveredProductReviewViewModel
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            ProductId = productId,
            ProductName = item.ProductName,
            ProductSlug = item.ProductSlug,
            ImageUrl = item.ImageUrl,
            ReviewId = existing?.Id,
            ReviewStatus = existing == null ? "NotReviewed" :
                existing.IsApproved && !existing.IsRejected ? "Approved" :
                existing.IsRejected ? "Rejected" : "Pending",
            Rating = existing?.Rating ?? 5,
            Title = existing?.Title,
            Comment = existing?.Comment
        };
    }

    [HttpPost("Cancel/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            await canceller.CancelUnpaidPendingOrder(id, UserId);
            TempData["OrderInfo"] = "Order cancelled. Its stock has been restored.";
        }
        catch (CheckoutException ex) { TempData["OrderInfo"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
}
