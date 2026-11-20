using System.Security.Claims;
using Jersey_ecommerce.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers;

[Authorize, Route("Wishlist")]
public sealed class WishlistController(ApplicationDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var saved = await db.WishListItems.AsNoTracking().AsSplitQuery()
            .Where(x => x.Wishlist.UserId == userId && x.Product.IsPublished)
            .Include(x => x.Product).ThenInclude(p => p.Images)
            .Include(x => x.Product).ThenInclude(p => p.Variants)
            .OrderByDescending(x => x.AddedAt).ToListAsync();
        return View(saved);
    }

    [HttpPost("Remove/{productId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var row = await db.WishListItems.FirstOrDefaultAsync(x => x.ProductId == productId && x.Wishlist.UserId == userId);
        if (row != null) { db.WishListItems.Remove(row); await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
