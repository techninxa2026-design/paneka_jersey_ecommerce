using System.Security.Claims;
using System.Data;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Checkout;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers;

[Authorize]
[Route("Checkout/Bkash")]
public sealed class BkashController(
    ApplicationDbContext db,
    BkashGateway gateway,
    ILogger<BkashController> logger) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("Start/{id:int}")]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Payment)
            .SingleOrDefaultAsync(o => o.Id == id && o.UserId == UserId, ct);
        if (order == null) return NotFound();
        if (order.Payment is null || order.Payment.Method != "Bkash")
            return BadRequest("This order is not a bKash order.");
        if (order.Payment.Status == "Paid")
            return RedirectToAction("Success", "Checkout", new { id });
        if (order.Payment.Status != "Pending" || order.Status == "Cancelled")
            return BadRequest("This order can no longer be paid online.");

        // Keep users on the same pending checkout rather than creating a fresh charge.
        var latest = await db.BkashPaymentAttempts
            .Where(x => x.OrderId == id)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (latest != null && latest.Status == "Creating")
        {
            // Even an old Creating row may represent an in-flight remote call.
            // Never initiate a second charge automatically without reconciliation.
            TempData["CheckoutError"] = latest.CreatedAt <= DateTime.UtcNow.AddMinutes(-2)
                ? "The previous payment initiation was interrupted. Contact support to reconcile it before retrying."
                : "Your bKash checkout is being initialized. Please check its status shortly.";
            return RedirectToAction(nameof(Status), new { id });
        }

        if (latest != null && latest.Status == "Initiated")
        {
            // Reconcile the prior attempt before considering another charge.
            if (await VerifyAttempt(latest.Id, ct))
                return RedirectToAction("Success", "Checkout", new { id });
            if (latest.CreatedAt > DateTime.UtcNow.AddMinutes(-15) &&
                BkashGateway.IsTrustedCheckoutUrl(latest.CheckoutUrl))
                return Redirect(latest.CheckoutUrl!);
            TempData["CheckoutError"] =
                "Your earlier bKash attempt is not verified yet. Do not pay again until its status has been checked.";
            return RedirectToAction(nameof(Status), new { id });
        }
        if (latest != null && latest.Status == "Uncertain")
        {
            TempData["CheckoutError"] =
                "The earlier gateway request had an uncertain result. Contact support before retrying payment.";
            return RedirectToAction(nameof(Status), new { id });
        }

        var attempt = new BkashPaymentAttempt
        {
            OrderId = id,
            Nonce = Guid.NewGuid().ToString("N"),
            Status = "Creating"
        };
        db.BkashPaymentAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        try
        {
            // The base callback URL comes from trusted configuration, never from Request.Host.
            var sep = gateway.CallbackUrl.Contains('?') ? "&" : "?";
            var callback = $"{gateway.CallbackUrl}{sep}attemptId={attempt.Id}&nonce={attempt.Nonce}";

            // The amount and invoice are taken from the database, never from the browser.
            var created = await gateway.CreateAsync(
                order.TotalAmount,
                order.OrderNumber,
                order.CustomerPhone ?? order.OrderNumber,
                callback,
                ct);

            attempt.GatewayPaymentId = created.PaymentId;
            attempt.CheckoutUrl = created.RedirectUrl;
            attempt.Status = "Initiated";
            await db.SaveChangesAsync(ct);
            return Redirect(created.RedirectUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogError(ex, "bKash create failed or uncertain for order {OrderId}, attempt {AttemptId}", id, attempt.Id);
            // An HTTP transport error may happen after bKash created a payment.
            // Do not automatically create another potential charge in this situation.
            attempt.Status = ex is InvalidOperationException ? "CreateFailed" : "Uncertain";
            await db.SaveChangesAsync(CancellationToken.None);
            TempData["CheckoutError"] = attempt.Status == "Uncertain"
                ? "The gateway response was interrupted. Your order is unpaid locally; contact support before retrying."
                : "Could not open bKash. Verify your sandbox credentials/configuration and try again.";
            return RedirectToAction(nameof(Status), new { id });
        }
    }

    // bKash redirects the shopper to this URL after hosted checkout.
    // A callback parameter NEVER marks the order paid; we call bKash server-to-server.
    [AllowAnonymous]
    [HttpGet("Callback")]
    public async Task<IActionResult> Callback(
        int attemptId, string? nonce, string? paymentID, string? status,
        CancellationToken ct)
    {
        var attempt = await db.BkashPaymentAttempts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == attemptId && x.Nonce == nonce, ct);
        if (attempt is null || string.IsNullOrWhiteSpace(nonce)) return NotFound();
        if (string.IsNullOrWhiteSpace(attempt.GatewayPaymentId) ||
            !string.Equals(paymentID, attempt.GatewayPaymentId, StringComparison.Ordinal))
            return BadRequest("Payment reference did not match the checkout session.");

        if (attempt.Status == "Completed")
            return RedirectToAction("Success", "Checkout", new { id = attempt.OrderId });

        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Execute the payment once after the hosted page reports success.
                await gateway.ExecuteAsync(attempt.GatewayPaymentId, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                // Execution might have succeeded remotely even if the response was lost.
                // Always follow with an independently verified Query call.
                logger.LogWarning(ex, "bKash execute uncertain for attempt {AttemptId}", attempt.Id);
            }
        }

        var verified = await VerifyAttempt(attempt.Id, ct);
        if (verified)
            return RedirectToAction("Success", "Checkout", new { id = attempt.OrderId });

        return RedirectToAction(nameof(Status), new { id = attempt.OrderId });
    }

    // Shopper can check an existing pending transaction after losing the callback.
    [HttpGet("Verify/{id:int}")]
    public async Task<IActionResult> Verify(int id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Payment)
            .SingleOrDefaultAsync(o => o.Id == id && o.UserId == UserId, ct);
        if (order is null) return NotFound();
        if (order.Payment?.Status == "Paid")
            return RedirectToAction("Success", "Checkout", new { id });
        if (order.Payment?.Method != "Bkash") return BadRequest();

        var last = await db.BkashPaymentAttempts.AsNoTracking()
            .Where(a => a.OrderId == id && a.GatewayPaymentId != null)
            .OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        if (last != null && await VerifyAttempt(last.Id, ct))
            return RedirectToAction("Success", "Checkout", new { id });
        return RedirectToAction(nameof(Status), new { id });
    }

    [HttpGet("Status/{id:int}")]
    public async Task<IActionResult> Status(int id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Payment)
            .SingleOrDefaultAsync(o => o.Id == id && o.UserId == UserId, ct);
        if (order is null) return NotFound();
        if (order.Payment?.Method != "Bkash") return BadRequest();
        if (order.Payment.Status == "Paid")
            return RedirectToAction("Success", "Checkout", new { id });

        return View("~/Views/Checkout/BkashStatus.cshtml", new BkashStatusModel(
            order.Id, order.OrderNumber, order.TotalAmount,
            TempData["CheckoutError"]?.ToString()));
    }

    // Returns true ONLY after the payment ID, invoice, amount, currency and status
    // are independently confirmed with the gateway. Never trust the browser redirect.
    private async Task<bool> VerifyAttempt(int attemptId, CancellationToken ct)
    {
        var attempt = await db.BkashPaymentAttempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt == null || string.IsNullOrWhiteSpace(attempt.GatewayPaymentId)) return false;

        var order = await db.Orders.AsNoTracking().Include(o => o.Payment)
            .SingleOrDefaultAsync(o => o.Id == attempt.OrderId, ct);
        if (order?.Payment is null || order.Payment.Method != "Bkash") return false;
        if (order.Payment.Status == "Paid") return true;
        if (order.Payment.Status != "Pending" || order.Status == "Cancelled") return false;

        BkashResult result;
        try { result = await gateway.QueryAsync(attempt.GatewayPaymentId, ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Unable to confirm bKash attempt {AttemptId}", attemptId);
            return false;
        }

        bool valid = result.Valid &&
            string.Equals(result.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.PaymentId, attempt.GatewayPaymentId, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(result.TransactionId) &&
            string.Equals(result.Currency, "BDT", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.MerchantInvoice, order.OrderNumber, StringComparison.Ordinal) &&
            result.Amount == order.TotalAmount && result.Amount == order.Payment.Amount;

        if (!valid) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var updated = await db.Payments
            .Where(p => p.OrderId == order.Id && p.Method == "Bkash" && p.Status == "Pending" &&
                        p.Amount == result.Amount && p.Order.Status == "Pending")
            .ExecuteUpdateAsync(up => up
                .SetProperty(p => p.Status, "Paid")
                .SetProperty(p => p.Provider, "bKash")
                .SetProperty(p => p.ProviderPaymentId, result.PaymentId)
                .SetProperty(p => p.TransactionReference, result.TransactionId)
                .SetProperty(p => p.PaidAt, DateTime.UtcNow), ct);

        if (updated == 1)
        {
            var completedAt = DateTime.UtcNow;
            // Keep Order and Payment in sync, in the SAME database transaction.
            await db.Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(up => up
                    .SetProperty(o => o.PaidAt, completedAt)
                    .SetProperty(o => o.UpdatedAt, completedAt), ct);
            await db.BkashPaymentAttempts.Where(a => a.Id == attemptId)
                .ExecuteUpdateAsync(up => up
                    .SetProperty(a => a.Status, "Completed")
                    .SetProperty(a => a.GatewayTransactionId, result.TransactionId)
                    .SetProperty(a => a.CompletedAt, completedAt), ct);
            // Reuse the project's existing payment-audit table.
            // This insert only occurs after the Pending -> Paid compare-and-swap.
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                PaymentId = order.Payment.Id,
                TransactionType = "Payment",
                Status = "Completed",
                ProviderTransactionId = result.TransactionId,
                Reference = result.PaymentId,
                Amount = result.Amount,
                CreatedAt = completedAt,
                CompletedAt = completedAt
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }

        await transaction.RollbackAsync(ct);
        return await db.Payments.AsNoTracking().AnyAsync(
            p => p.OrderId == order.Id && p.Method == "Bkash" && p.Status == "Paid" &&
                 p.ProviderPaymentId == result.PaymentId && p.TransactionReference == result.TransactionId, ct);
    }
}

public sealed record BkashStatusModel(int OrderId, string OrderNumber, decimal Total, string? Error);
