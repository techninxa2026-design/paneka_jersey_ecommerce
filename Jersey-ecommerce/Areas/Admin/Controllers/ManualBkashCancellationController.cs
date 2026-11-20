using System.Security.Claims;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jersey_ecommerce.Areas.Admin.Controllers;

/// <summary>
/// Allows staff to release unpaid reserved stock only after independently
/// confirming no manual bKash transfer reached the merchant account.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Orders/CancelPendingBkash")]
public sealed class ManualBkashCancellationController(OrderCancellationService cancellations) : Controller
{
    [HttpPost("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int id, bool financeCleared, string? reconciliationNote)
    {
        if (!financeCleared || string.IsNullOrWhiteSpace(reconciliationNote) ||
            reconciliationNote.Trim().Length is < 10 or > 450)
        {
            TempData["Error"] = "Check your actual bKash receiving wallet and record 10–450 characters of no-funds reconciliation before cancelling.";
            return RedirectToAction("Details", "Orders", new { area = "Admin", id });
        }

        try
        {
            var staff = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin";
            await cancellations.CancelUnpaidPendingOrder(
                id, staff, asAdmin: true, financeNote: reconciliationNote);
            TempData["Success"] = "Unpaid manual bKash order cancelled after finance reconciliation. Reserved stock and coupon usage restored.";
        }
        catch (CheckoutException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction("Details", "Orders", new { area = "Admin", id });
    }
}
