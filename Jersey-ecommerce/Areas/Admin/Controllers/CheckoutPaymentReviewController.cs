using Jersey_ecommerce.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers;

// Manual verification only: an operator must verify a real external transaction first.
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/CheckoutPaymentReview")]
public sealed class CheckoutPaymentReviewController(ApplicationDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var pending = await db.Payments.AsNoTracking().Include(p => p.Order)
            .Where(p => p.Method == "ManualAdvance" && p.Status == "Pending")
            .OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync();
        return View(pending);
    }

    [HttpPost("Confirm/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id, string verificationNote)
    {
        if (string.IsNullOrWhiteSpace(verificationNote) || verificationNote.Trim().Length < 10)
        {
            TempData["Error"] = "Record the independent payment verification details (minimum 10 characters).";
            return RedirectToAction(nameof(Index));
        }
        await using var tx = await db.Database.BeginTransactionAsync();
        var payment = await db.Payments.Include(p => p.Order)
            .SingleOrDefaultAsync(p => p.Id == id);
        if (payment == null || payment.Method != "ManualAdvance" || payment.Status != "Pending")
            return NotFound();
        payment.Status = "Paid";
        payment.PaidAt = DateTime.UtcNow;
        payment.Order.Status = "Confirmed";
        payment.Order.UpdatedAt = DateTime.UtcNow;
        db.OrderStatusHistories.Add(new Jersey_ecommerce.Models.Orders.OrderStatusHistory
        {
            OrderId = payment.OrderId, Status = "Confirmed",
            Note = "Payment verified by admin: " + verificationNote.Trim()[..Math.Min(verificationNote.Trim().Length, 160)],
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Success"] = "Payment marked paid after independent verification.";
        return RedirectToAction(nameof(Index));
    }
}
