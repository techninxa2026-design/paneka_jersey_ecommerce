using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers;

/// <summary>
/// Independent staff reconciliation. Never trust a submitted TrxID as evidence
/// that money reached the store's bKash wallet.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/CheckoutPaymentReview")]
public sealed class CheckoutPaymentReviewController(
    ApplicationDbContext db, ManualBkashPaymentService manual) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1)
    {
        const int pageSize = 25;
        var query = db.Payments.AsNoTracking()
            .Where(p => (p.Method == "Bkash" && p.Status == "Submitted" &&
                         p.ManualSubmittedAt != null) ||
                        (p.Method == "ManualAdvance" && p.Status == "Pending"));
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewData["Page"] = page;
        ViewData["TotalPages"] = totalPages;
        ViewData["TotalPending"] = total;
        ViewData["ManualPending"] = await query.CountAsync(p => p.Method == "Bkash");
        ViewData["LegacyPending"] = await query.CountAsync(p => p.Method == "ManualAdvance");

        var queue = await query.Include(p => p.Order)
            .OrderBy(p => p.ManualSubmittedAt ?? p.CreatedAt).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return View(queue);
    }

    [HttpPost("Confirm/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id, string? verificationNote,
        bool independentlyVerified = false)
    {
        if (!independentlyVerified)
        {
            TempData["Error"] = "You must confirm you checked the receiving account outside PANENKA before marking a transfer paid.";
            return RedirectToAction(nameof(Index));
        }

        var method = await db.Payments.AsNoTracking()
            .Where(p => p.Id == id).Select(p => p.Method).FirstOrDefaultAsync();
        if (method == null) return NotFound();
        try
        {
            if (method == "Bkash")
            {
                await manual.VerifyAsync(id,
                    User.FindFirstValue(ClaimTypes.NameIdentifier)!, verificationNote);
                TempData["Success"] = "Manual bKash payment independently verified and order confirmed.";
            }
            else if (method == "ManualAdvance")
            {
                // Legacy records remain reconciliable, although new checkouts
                // offer only COD and manual bKash.
                var note = verificationNote?.Trim() ?? "";
                if (note.Length is < 10 or > 450)
                    throw new CheckoutException("Record independent verification details (10–450 characters).");
                await using var tx = await db.Database.BeginTransactionAsync();
                var payment = await db.Payments.Include(p => p.Order).SingleAsync(p => p.Id == id);
                if (payment.Status != "Pending" || payment.Order.Status != "Pending")
                    throw new CheckoutException("The payment or order is no longer pending.");
                var now = DateTime.UtcNow;
                payment.Status = "Paid";
                payment.PaidAt = now;
                payment.Order.Status = "Confirmed";
                payment.Order.PaidAt = now;
                payment.Order.UpdatedAt = now;
                db.PaymentTransactions.Add(new PaymentTransaction
                {
                    PaymentId = payment.Id, TransactionType = "ManualAdvance",
                    Status = "Verified", ProviderTransactionId = payment.TransactionReference,
                    Amount = payment.Amount, CreatedAt = now, CompletedAt = now
                });
                db.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = payment.OrderId, Status = "Confirmed",
                    ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                    Note = "Legacy advance payment independently verified by staff.",
                    CreatedAt = now
                });
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                TempData["Success"] = "Legacy advance payment independently verified.";
            }
            else
                throw new CheckoutException("This payment method is not eligible for manual reconciliation.");
        }
        catch (CheckoutException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Reject/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? verificationNote)
    {
        try
        {
            await manual.RejectAsync(id,
                User.FindFirstValue(ClaimTypes.NameIdentifier)!, verificationNote);
            TempData["Success"] = "Unverified transfer rejected. The customer can submit corrected payment details.";
        }
        catch (CheckoutException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
