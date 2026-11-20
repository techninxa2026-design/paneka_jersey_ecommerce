using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/OrderDelivery")]
public sealed class OrderDeliveryController(ApplicationDbContext db, OrderCancellationService canceller) : Controller
{
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Payment)
            .Include(o => o.Shipment).SingleOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();
        ViewBag.Order = order;
        return View(order);
    }

    [HttpPost("Cancel/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            await canceller.CancelUnpaidPendingOrder(id,
                User.FindFirstValue(ClaimTypes.NameIdentifier)!, asAdmin: true);
            TempData["OrderInfo"] = "Unpaid order cancelled, inventory released.";
        }
        catch (CheckoutException ex) { TempData["OrderInfo"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
}
