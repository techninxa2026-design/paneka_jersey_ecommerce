using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
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

            return View(new OrderDetailsViewModel
            {
                Order = order,
                NextStatus = NextStatuses.TryGetValue(order.Status, out var next)
                    ? next : null
            });
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
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound();

            // Advance-payment orders must not progress toward shipping until
            // their payment is independently verified. COD remains eligible.
            if (order.Payment == null ||
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

            try
            {
                var now = DateTime.UtcNow;
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
