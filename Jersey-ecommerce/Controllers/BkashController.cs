using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers;

/// <summary>
/// Manual bKash transfer screen. This controller never asks for a PIN/OTP,
/// initiates a gateway charge, or marks customer-submitted claims as paid.
/// </summary>
[Authorize]
[Route("Checkout/Bkash")]
public sealed class BkashController(
    ApplicationDbContext db,
    ManualBkashPaymentService manual,
    ILogger<BkashController> logger) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Retain pre-existing storefront links and URLs but lead to the manual screen.
    [HttpGet("Start/{id:int}")]
    public IActionResult Start(int id) => RedirectToAction(nameof(Status), new { id });

    [HttpGet("Verify/{id:int}")]
    public IActionResult Verify(int id) => RedirectToAction(nameof(Status), new { id });

    [HttpGet("Status/{id:int}")]
    public async Task<IActionResult> Status(int id)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Payment)
            .SingleOrDefaultAsync(o => o.Id == id && o.UserId == UserId);
        if (order == null) return NotFound();
        var payment = order.Payment;
        if (payment?.Method != "Bkash") return BadRequest("This is not a bKash order.");

        var viewModel = new ManualBkashStatusModel
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Amount = payment.Amount,
            PaymentStatus = payment.Status,
            OrderStatus = order.Status,
            RecipientNumber = payment.ManualRecipientNumber,
            Instructions = await manual.CurrentInstructionsAsync(),
            SenderNumber = payment.ManualSenderNumber,
            TransactionId = payment.TransactionReference,
            ClaimedPaidAtUtc = payment.ManualClaimedPaidAt,
            SubmittedAtUtc = payment.ManualSubmittedAt,
            ReviewedAtUtc = payment.ManualReviewedAt,
            ReviewNote = payment.ManualReviewNote,
            CanSubmit = order.Status == "Pending" &&
                (payment.Status is "Pending" or "Rejected") &&
                ManualBkashPaymentService.IsValidMobile(payment.ManualRecipientNumber),
            Error = TempData["CheckoutError"]?.ToString(),
            Notice = TempData["PaymentNotice"]?.ToString()
        };
        return View("~/Views/Checkout/BkashStatus.cshtml", viewModel);
    }

    [HttpPost("Submit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id, string? senderNumber,
        string? transactionId, DateTime paidAtLocal)
    {
        try
        {
            await manual.SubmitAsync(id, UserId, senderNumber, transactionId, paidAtLocal);
            TempData["PaymentNotice"] = "Your payment details were saved and sent for staff verification. Your order is not marked paid yet.";
        }
        catch (CheckoutException ex)
        {
            TempData["CheckoutError"] = ex.Message;
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Duplicate or concurrent manual bKash submission for order {OrderId}", id);
            TempData["CheckoutError"] = "This transaction ID may already be recorded. Check the number and try again or contact support.";
        }

        return RedirectToAction(nameof(Status), new { id });
    }
}

public sealed class ManualBkashStatusModel
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentStatus { get; set; } = "Pending";
    public string OrderStatus { get; set; } = "Pending";
    public string? RecipientNumber { get; set; }
    public string Instructions { get; set; } = "";
    public string? SenderNumber { get; set; }
    public string? TransactionId { get; set; }
    public DateTime? ClaimedPaidAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public bool CanSubmit { get; set; }
    public string? Error { get; set; }
    public string? Notice { get; set; }
}
