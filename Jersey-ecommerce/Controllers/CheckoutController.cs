using System.Security.Claims;
using System.Text.Json;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Checkout;
using Jersey_ecommerce.Services.Checkout;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.ViewModels.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers;

[Authorize]
[Route("Checkout")]
public sealed class CheckoutController(
    CheckoutService cart,
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    ILogger<CheckoutController> logger) : Controller
{
    // Bind checkout details to the signed-in account rather than just the browser session.
    // This avoids exposing another customer's delivery details when users switch accounts.
    private string DeliverySession => "PANENKA_DELIVERY_V2_" + UserId;
    private string OrderTokenSession => "PANENKA_ORDER_TOKEN_V2_" + UserId;
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(Delivery));

    // Uses the SAME Admin Settings / free-shipping rules as PlaceOrder().
    // CheckoutOptions region overrides are nullable; never cast decimal? to decimal.
    private async Task<(decimal Dhaka, decimal OutsideDhaka)?> GetRates(decimal subtotal)
    {
        var rates = await cart.ShippingRates(subtotal);
        if (rates.Dhaka < 0m || rates.OutsideDhaka < 0m) return null;
        return rates;
    }

    [HttpGet("Delivery")]
    public async Task<IActionResult> Delivery()
    {
        var quote = await cart.GetCart(UserId);
        if (quote.Items.Count == 0) return RedirectToAction("Index", "Cart");
        var rates = await GetRates(quote.Subtotal);
        if (rates is null) return StatusCode(503, "Shipping fees are not configured.");
        var user = await users.GetUserAsync(User);
        var saved = ReadDelivery() ?? new DeliveryInput
        {
            FullName = user?.FullName ?? "",
            Email = user?.Email ?? "",
            Phone = user?.PhoneNumber ?? ""
        };
        return View(new DeliveryPageViewModel
        {
            Cart = quote, Delivery = saved,
            DhakaFee = rates.Value.Dhaka, OutsideDhakaFee = rates.Value.OutsideDhaka
        });
    }

    [HttpPost("Delivery")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delivery(DeliveryPageViewModel model)
    {
        var quote = await cart.GetCart(UserId);
        if (quote.Items.Count == 0) return RedirectToAction("Index", "Cart");
        var rates = await GetRates(quote.Subtotal);
        if (rates is null) return StatusCode(503, "Shipping fees are not configured.");

        if (model.Delivery is null)
            ModelState.AddModelError(nameof(model.Delivery), "Delivery details are required.");
        else
        {
            var canonicalDistrict = BangladeshDistricts.Normalize(model.Delivery.District);
            if (canonicalDistrict is not null)
                model.Delivery.District = canonicalDistrict;
            if (model.Delivery.Region is not ("Dhaka" or "OutsideDhaka"))
                ModelState.AddModelError("Delivery.Region", "Select a valid shipping region.");
            if (model.Delivery.Region == "Dhaka" &&
                !string.Equals(model.Delivery.District?.Trim(), "Dhaka", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError("Delivery.Region", "Inside Dhaka requires Dhaka district.");
        }
        if (!ModelState.IsValid)
        {
            return View(new DeliveryPageViewModel
            {
                Delivery = model.Delivery ?? new DeliveryInput(), Cart = quote,
                DhakaFee = rates.Value.Dhaka, OutsideDhakaFee = rates.Value.OutsideDhaka
            });
        }

        HttpContext.Session.SetString(DeliverySession, JsonSerializer.Serialize(model.Delivery));
        HttpContext.Session.Remove(OrderTokenSession);
        return RedirectToAction(nameof(Payment));
    }

    [HttpGet("Payment")]
    public async Task<IActionResult> Payment()
    {
        var delivery = ReadDelivery();
        if (delivery is null || delivery.Region is not ("Dhaka" or "OutsideDhaka") ||
            !BangladeshDistricts.IsValid(delivery.District) ||
            (delivery.Region == "Dhaka" && !BangladeshDistricts.IsDhaka(delivery.District)))
            return RedirectToAction(nameof(Delivery));
        var quote = await cart.GetCart(UserId);
        if (quote.Items.Count == 0) return RedirectToAction("Index", "Cart");
        var rates = await GetRates(quote.Subtotal);
        if (rates is null) return StatusCode(503, "Shipping fees are not configured.");
        var token = HttpContext.Session.GetString(OrderTokenSession);
        if (!Guid.TryParseExact(token, "N", out _))
        {
            token = Guid.NewGuid().ToString("N");
            HttpContext.Session.SetString(OrderTokenSession, token);
        }
        return View(new PaymentPageViewModel
        {
            Delivery = delivery, Cart = quote, SubmissionToken = token!,
            ShippingCost = delivery.Region == "Dhaka" ? rates.Value.Dhaka : rates.Value.OutsideDhaka,
            AllowCashOnDelivery = await cart.CodEnabled(),
            ManualBkashNumber = await cart.ManualBkashNumber(),
            ManualBkashInstructions = await cart.ManualBkashInstructions(),
            ManualInstructions = cart.Options.ManualPaymentInstructions
        });
    }

    // Reserve inventory, coupon and a pending order before displaying transfer
    // instructions. The browser popup collects TrxID only AFTER the order exists.
    [HttpPost("Payment/Bkash/Prepare")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepareBkash(string? submissionToken, string? bkashRecipientNumber)
    {
        var delivery = ReadDelivery();
        if (delivery is null || delivery.Region is not ("Dhaka" or "OutsideDhaka") ||
            !BangladeshDistricts.IsValid(delivery.District) ||
            (delivery.Region == "Dhaka" && !BangladeshDistricts.IsDhaka(delivery.District)))
            return BadRequest(new { message = "Your delivery details need updating. Return to Delivery and try again." });

        var storedToken = HttpContext.Session.GetString(OrderTokenSession);
        if (!Guid.TryParseExact(submissionToken, "N", out _) ||
            !string.Equals(storedToken, submissionToken, StringComparison.Ordinal))
            return BadRequest(new { message = "Your checkout session expired. Reload the payment page." });

        var input = new PlaceOrderInput
        {
            PaymentMethod = "Bkash", SubmissionToken = submissionToken!,
            BkashRecipientNumber = bkashRecipientNumber
        };

        try
        {
            var orderId = await cart.PlaceOrder(UserId, delivery, input, prepareManualBkash: true);
            var order = await db.Orders.AsNoTracking().Include(o => o.Payment)
                .SingleAsync(o => o.Id == orderId && o.UserId == UserId);
            if (order.Payment?.Method != "Bkash")
                return Conflict(new { message = "This checkout belongs to a different payment method. Open My Orders." });

            // Once submitted, do not offer to pay again against a completed or
            // already-reported payment. Returning the order ID allows resume.
            return Ok(new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                recipientNumber = order.Payment.ManualRecipientNumber,
                amount = order.Payment.Amount,
                paymentStatus = order.Payment.Status
            });
        }
        catch (CheckoutException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not prepare manual bKash order for customer {UserId}", UserId);
            var prior = await FindPreviousOrder(submissionToken ?? "");
            if (prior is not null && prior.Value.Method == "Bkash")
            {
                var order = await db.Orders.AsNoTracking().Include(o => o.Payment)
                    .SingleAsync(o => o.Id == prior.Value.OrderId && o.UserId == UserId);
                return Ok(new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    recipientNumber = order.Payment?.ManualRecipientNumber,
                    amount = order.Payment?.Amount,
                    paymentStatus = order.Payment?.Status
                });
            }
            return StatusCode(503, new { message = "Could not reserve this order. No transfer should be made until checkout confirms the reservation. Check My Orders before retrying." });
        }
    }

    [HttpPost("Payment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Payment(PlaceOrderInput input)
    {
        var delivery = ReadDelivery();
        if (delivery is null || delivery.Region is not ("Dhaka" or "OutsideDhaka") ||
            !BangladeshDistricts.IsValid(delivery.District) ||
            (delivery.Region == "Dhaka" && !BangladeshDistricts.IsDhaka(delivery.District)))
            return RedirectToAction(nameof(Delivery));

        var storedToken = HttpContext.Session.GetString(OrderTokenSession);
        if (!Guid.TryParseExact(input.SubmissionToken, "N", out _) ||
            string.IsNullOrWhiteSpace(storedToken) ||
            !string.Equals(input.SubmissionToken, storedToken, StringComparison.Ordinal))
        {
            TempData["CheckoutError"] = "Your checkout session expired. Reload the payment step.";
            return RedirectToAction(nameof(Payment));
        }
        if (!ModelState.IsValid || input.PaymentMethod is not ("COD" or "Bkash"))
        {
            TempData["CheckoutError"] = "Choose cash on delivery or bKash.";
            return RedirectToAction(nameof(Payment));
        }
        if (input.PaymentMethod == "COD" && !await cart.CodEnabled())
        {
            TempData["CheckoutError"] = "Cash on delivery is disabled in store settings.";
            return RedirectToAction(nameof(Payment));
        }
        if (input.PaymentMethod == "Bkash" && await cart.ManualBkashNumber() is null)
        {
            TempData["CheckoutError"] = "Manual bKash is not currently available. Choose cash on delivery or contact support.";
            return RedirectToAction(nameof(Payment));
        }

        try
        {
            // CheckoutService atomically creates the order, stock movements, and Pending
            // payment using the requested method. No second, non-transactional conversion.
            var orderId = await cart.PlaceOrder(UserId, delivery, input);
            ClearCheckout();
            return input.PaymentMethod == "Bkash"
                ? RedirectToAction("Status", "Bkash", new { id = orderId })
                : RedirectToAction(nameof(Success), new { id = orderId });
        }
        catch (CheckoutException ex)
        {
            TempData["CheckoutError"] = ex.Message;
            return RedirectToAction(nameof(Payment));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checkout submission failed for customer {UserId}", UserId);
            // The current database uses OrderNumber = PNK + token plus a unique index.
            // This is the existing idempotency mechanism. No CheckoutSubmission table
            // or schema migration is required.
            var prior = await FindPreviousOrder(input.SubmissionToken);
            if (prior is not null)
            {
                ClearCheckout();
                if (input.PaymentMethod == "Bkash" && prior.Value.Method == "Bkash")
                    return RedirectToAction("Status", "Bkash", new { id = prior.Value.OrderId });
                if (input.PaymentMethod == "COD" && prior.Value.Method == "CashOnDelivery")
                    return RedirectToAction(nameof(Success), new { id = prior.Value.OrderId });

                TempData["OrderInfo"] = "An order already exists for this submission. Please review it before trying again.";
                return RedirectToAction("Details", "MyOrders", new { id = prior.Value.OrderId });
            }
            TempData["CheckoutError"] = "Could not confirm the order. Check My Orders before submitting again.";
            return RedirectToAction(nameof(Payment));
        }
    }

    [HttpGet("Success/{id:int}")]
    public async Task<IActionResult> Success(int id)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Payment)
            .SingleOrDefaultAsync(o => o.Id == id && o.UserId == UserId);
        if (order is null) return NotFound();
        // Do not display the bKash success page for an unverified transaction.
        if (order.Payment?.Method == "Bkash" && order.Payment.Status != "Paid")
            return RedirectToAction("Status", "Bkash", new { id });
        return View(new OrderSuccessViewModel
        {
            OrderNumber = order.OrderNumber,
            Total = order.TotalAmount,
            PaymentMethod = order.Payment?.Method ?? "Unknown",
            PaymentStatus = order.Payment?.Status ?? "Pending",
            ManualInstructions = order.Payment?.Method == "ManualAdvance"
                ? cart.Options.ManualPaymentInstructions : ""
        });
    }

    private async Task<(int OrderId, string Method)?> FindPreviousOrder(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) return null;
        var orderNumber = "PNK-" + token[..24].ToUpperInvariant();
        var found = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == UserId && o.OrderNumber == orderNumber)
            .Select(o => new { o.Id, Method = o.Payment != null ? o.Payment.Method : "" })
            .SingleOrDefaultAsync();
        return found is null ? null : (found.Id, found.Method);
    }

    private DeliveryInput? ReadDelivery()
    {
        var json = HttpContext.Session.GetString(DeliverySession);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<DeliveryInput>(json); }
        catch (JsonException) { return null; }
    }

    private void ClearCheckout()
    {
        HttpContext.Session.Remove(DeliverySession);
        HttpContext.Session.Remove(OrderTokenSession);
    }
}
