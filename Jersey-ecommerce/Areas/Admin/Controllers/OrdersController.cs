using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<OrdersController> _logger;
        private const int PageSize = 15;

        // Single-step progression only. Payment, refunds, cancellation and inventory
        // are deliberately NOT modified by this controller.
        private static readonly IReadOnlyDictionary<string, string> NextStatuses =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pending"] = "Confirmed",
                ["Confirmed"] = "Processing",
                ["Processing"] = "Shipped",
                ["Shipped"] = "Delivered"
            };

        public OrdersController(ApplicationDbContext context,
                                ILogger<OrdersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET /admin/orders
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search, string? status, DateTime? from, DateTime? to, int page = 1)
        {
            search = search?.Trim();
            status = status?.Trim();
            page = Math.Max(1, page);

            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            {
                (from, to) = (to, from);
            }

            IQueryable<Order> filtered = _context.Orders.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(o =>
                    o.OrderNumber.Contains(search) ||
                    o.CustomerName.Contains(search) ||
                    o.CustomerEmail.Contains(search) ||
                    o.CustomerPhone.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(status))
                filtered = filtered.Where(o => o.Status == status);

            if (from.HasValue)
                filtered = filtered.Where(o => o.CreatedAt >= from.Value.Date);

            if (to.HasValue && to.Value.Date < DateTime.MaxValue.Date)
            {
                var exclusiveEnd = to.Value.Date.AddDays(1);
                filtered = filtered.Where(o => o.CreatedAt < exclusiveEnd);
            }

            var baseQuery = _context.Orders.AsNoTracking();
            var filteredCount = await filtered.CountAsync();
            var maxPage = Math.Max(1, (int)Math.Ceiling((double)filteredCount / PageSize));
            page = Math.Min(page, maxPage);

            var rows = await filtered
                .OrderByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(o => new OrderListRowViewModel
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerName = o.CustomerName,
                    CustomerEmail = o.CustomerEmail,
                    Status = o.Status,
                    PaymentStatus = o.Payment != null ? o.Payment.Status : null,
                    ShipmentStatus = o.Shipment != null ? o.Shipment.Status : null,
                    TotalAmount = o.TotalAmount,
                    ItemCount = o.Items.Count,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            var vm = new OrdersIndexViewModel
            {
                Orders = rows,
                Search = search,
                Status = status,
                From = from,
                To = to,
                Page = page,
                PageSize = PageSize,
                FilteredCount = filteredCount,
                TotalCount = await baseQuery.CountAsync(),
                PendingCount = await baseQuery.CountAsync(o => o.Status == "Pending"),
                SubmittedBkashCount = await baseQuery.CountAsync(o => o.Payment != null && o.Payment.Method == "Bkash" && o.Payment.Status == "Submitted" && o.Status == "Pending"),
                ProcessingCount = await baseQuery.CountAsync(o => o.Status == "Processing"),
                ShippedCount = await baseQuery.CountAsync(o => o.Status == "Shipped"),
                DeliveredCount = await baseQuery.CountAsync(o => o.Status == "Delivered"),
                RecordedOrderValue = await baseQuery.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m,
                StatusOptions = await baseQuery.Select(o => o.Status)
                    .Distinct().OrderBy(s => s).ToListAsync()
            };
            return View(vm);
        }

        // GET /admin/orders/details/123
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .AsNoTracking()
                .AsSplitQuery()
                .Include(o => o.Items)
                .Include(o => o.Payment)
                .Include(o => o.Shipment)
                .Include(o => o.StatusHistory)
                    .ThenInclude(h => h.ChangedByUser)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound();

            string? next = NextStatuses.TryGetValue(order.Status, out var value) ? value : null;
            string? hint = null;
            if (next != null &&
                (order.Payment == null || order.Payment.Status is "Cancelled" or "Failed" or "Refunded" ||
                 (order.Payment.Method is "Bkash" or "ManualAdvance" && order.Payment.Status != "Paid")))
            {
                hint = order.Payment?.Method == "Bkash" && order.Payment.Status == "Submitted"
                    ? "bKash transfer submitted. Verify the payment in the review section below before confirming the order."
                    : order.Payment?.Method == "Bkash" && order.Payment.Status == "Rejected"
                        ? "This bKash claim was rejected. Await corrected transaction details from the customer."
                        : order.Payment?.Method == "Bkash" && order.Payment.Status == "Pending"
                            ? "The buyer has not submitted a bKash transfer reference yet. Fulfillment is on hold."
                            : "Verify the advance payment before changing fulfillment. Cash-on-delivery orders can proceed without online payment.";
            }
            else if (next == "Shipped" &&
                (order.Shipment == null ||
                 order.Shipment.Status is not ("Packed" or "Shipped" or "InTransit" or "OutForDelivery") ||
                 string.IsNullOrWhiteSpace(order.Shipment.CourierName)))
            {
                hint = "Open Shipments, mark the package Packed and enter a courier before dispatching.";
            }
            else if (next == "Delivered" &&
                (order.Shipment == null ||
                 order.Shipment.Status is not ("Shipped" or "InTransit" or "OutForDelivery" or "Delivered") ||
                 string.IsNullOrWhiteSpace(order.Shipment.CourierName)))
            {
                hint = "Open Shipments and complete courier dispatch before marking this order delivered.";
            }

            return View(new OrderDetailsViewModel
            {
                Order = order,
                NextStatus = hint == null ? next : null,
                NextStatusHint = hint
            });
        }

        // POST /Admin/Orders/VerifyBkash/123
        // Admin can reconcile a customer transfer directly from Order Details.
        // Uses the SAME service as the central bKash verification queue; the
        // payment, order, audit log and customer-visible timeline stay atomic.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyBkash(int id, string? verificationNote,
            bool independentlyVerified = false)
        {
            var payment = await _context.Payments.AsNoTracking()
                .Where(p => p.OrderId == id && p.Method == "Bkash")
                .Select(p => new { p.Id, p.Status }).SingleOrDefaultAsync();
            if (payment == null) return NotFound();

            if (!independentlyVerified)
            {
                TempData["Error"] = "You must independently verify the full amount and TrxID in the receiving bKash wallet before confirming this order.";
                return RedirectToAction(nameof(Details), new { id, section = "payment-review" });
            }
            try
            {
                await new ManualBkashPaymentService(_context).VerifyAsync(
                    payment.Id, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin",
                    verificationNote);
                TempData["Success"] = "bKash payment verified. This order is now Confirmed and can move to Processing. The customer can see the updated status.";
            }
            catch (CheckoutException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Unable to reconcile bKash payment on order {OrderId}", id);
                TempData["Error"] = "Could not record the payment verification. Refresh the order and try again.";
            }
            return RedirectToAction(nameof(Details), new { id, section = "payment-review" });
        }

        // POST /Admin/Orders/RejectBkash/123
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectBkash(int id, string? verificationNote)
        {
            var payment = await _context.Payments.AsNoTracking()
                .Where(p => p.OrderId == id && p.Method == "Bkash")
                .Select(p => new { p.Id }).SingleOrDefaultAsync();
            if (payment == null) return NotFound();

            try
            {
                await new ManualBkashPaymentService(_context).RejectAsync(
                    payment.Id, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin",
                    verificationNote);
                TempData["Success"] = "Unverified bKash claim rejected with an audit note. The order remains Pending; the customer can correct and resubmit the transaction details.";
            }
            catch (CheckoutException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Unable to reject bKash claim on order {OrderId}", id);
                TempData["Error"] = "Could not save the rejection. Refresh the order and try again.";
            }
            return RedirectToAction(nameof(Details), new { id, section = "payment-review" });
        }

        // POST /admin/orders/changestatus/123
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus(int id, OrderStatusUpdateViewModel model)
        {
            if (id != model.OrderId || model.OrderId <= 0)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Invalid status form. Check the order and try again.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var order = await _context.Orders.Include(o => o.Payment)
                .Include(o => o.Shipment)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound();

            // Advance-payment orders must not progress toward shipping until
            // their payment is independently verified. COD remains eligible.
            if (order.Payment == null ||
                order.Payment.Status is "Cancelled" or "Failed" or "Refunded" ||
                ((order.Payment.Method == "Bkash" || order.Payment.Method == "ManualAdvance")
                  && order.Payment.Status != "Paid"))
            {
                TempData["Error"] = "Online/advance payment has not been confirmed; cannot advance this order.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (!string.Equals(order.Status, model.ExpectedStatus, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "This order's status changed since the page was opened. Review the latest status before saving.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (!NextStatuses.TryGetValue(order.Status, out var allowedNext) ||
                !string.Equals(model.NewStatus, allowedNext, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "This status change is not allowed. Orders can only move to the next fulfillment stage.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Checkout creates one shipment per order. Keep both records in sync
            // regardless of whether the operator advances from Orders or Shipments.
            if (allowedNext is "Shipped" or "Delivered")
            {
                if (order.Shipment == null || order.Shipment.Status is "Cancelled" or "Returned")
                {
                    TempData["Error"] = "A valid shipment is required before dispatch or delivery.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                if (string.IsNullOrWhiteSpace(order.Shipment.CourierName))
                {
                    TempData["Error"] = "Add courier details in Shipments before dispatching this order.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                if (allowedNext == "Shipped" &&
                    order.Shipment.Status is not ("Packed" or "Shipped" or "InTransit" or "OutForDelivery"))
                {
                    TempData["Error"] = "Pack the shipment in Shipments before dispatch. Pending and failed shipments cannot be marked shipped.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                if (allowedNext == "Delivered" &&
                    order.Shipment.Status is not ("Shipped" or "InTransit" or "OutForDelivery" or "Delivered"))
                {
                    TempData["Error"] = "A shipment cannot be marked delivered before dispatch or while it is failed.";
                    return RedirectToAction(nameof(Details), new { id });
                }
            }

            try
            {
                var now = DateTime.UtcNow;
                if (allowedNext is "Shipped" or "Delivered" && order.Shipment != null)
                {
                    order.Shipment.Status = allowedNext;
                    order.Shipment.ShippedAt ??= now;
                    if (allowedNext == "Delivered")
                        order.Shipment.DeliveredAt ??= now;
                }
                order.Status = allowedNext;
                order.UpdatedAt = now;

                _context.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    Status = allowedNext,
                    Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim(),
                    ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                    CreatedAt = now
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Order {order.OrderNumber} moved to {allowedNext}.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Could not change status of order {OrderId}", id);
                TempData["Error"] = "The status could not be saved. Check the server logs.";
            }

            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
