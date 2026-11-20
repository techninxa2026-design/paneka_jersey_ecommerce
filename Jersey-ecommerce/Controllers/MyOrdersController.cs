using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Services.Checkout;
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
    public async Task<IActionResult> Index()
    {
        var orders = await db.Orders.AsNoTracking().Include(o => o.Payment)
            .Where(o => o.UserId == UserId).OrderByDescending(o => o.CreatedAt)
            .Take(50).ToListAsync();
        return View(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var order = await db.Orders.AsNoTracking().AsSplitQuery()
            .Include(o => o.Payment).Include(o => o.Shipment)
            .Include(o => o.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
            .SingleOrDefaultAsync(o => o.Id == id && o.UserId == UserId);
        if (order == null) return NotFound();
        return View(order);
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
