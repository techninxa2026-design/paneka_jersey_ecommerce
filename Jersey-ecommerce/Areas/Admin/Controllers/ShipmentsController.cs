using System.Security.Claims;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class ShipmentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ShipmentsController> _logger;
    private const int PageSize = 12;

    public ShipmentsController(ApplicationDbContext context, ILogger<ShipmentsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // GET: /Admin/Shipments
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? status, int page = 1)
    {
        search = search?.Trim();
        if (!ShipmentStatuses.IsValid(status)) status = null;

        var query = _context.Shipments.AsNoTracking().AsQueryable();
        var allCount = await query.CountAsync();
        var pendingCount = await query.CountAsync(s => s.Status == "Pending" || s.Status == "Packed");
        var onTheWayCount = await query.CountAsync(s =>
            s.Status == "Shipped" || s.Status == "InTransit" || s.Status == "OutForDelivery");
        var deliveredCount = await query.CountAsync(s => s.Status == "Delivered");

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s =>
                s.Order.OrderNumber.Contains(search) ||
                s.Order.CustomerName.Contains(search) ||
                (s.CourierName != null && s.CourierName.Contains(search)) ||
                (s.TrackingNumber != null && s.TrackingNumber.Contains(search)));
        }

        if (status != null)
            query = query.Where(s => s.Status == status);

        var totalRecords = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRecords / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var rows = await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(s => new ShipmentListRowViewModel
            {
                Id = s.Id,
                OrderId = s.OrderId,
                OrderNumber = s.Order.OrderNumber,
                CustomerName = s.Order.CustomerName,
                Status = s.Status,
                CourierName = s.CourierName,
                TrackingNumber = s.TrackingNumber,
                CreatedAt = s.CreatedAt,
                ShippedAt = s.ShippedAt,
                EstimatedDeliveryAt = s.EstimatedDeliveryAt,
                DeliveredAt = s.DeliveredAt
            })
            .ToListAsync();

        return View(new ShipmentListViewModel
        {
            Search = search,
            Status = status,
            Page = page,
            PageSize = PageSize,
            TotalPages = totalPages,
            TotalRecords = totalRecords,
            AllCount = allCount,
            PendingCount = pendingCount,
            OnTheWayCount = onTheWayCount,
            DeliveredCount = deliveredCount,
            Rows = rows
        });
    }

    // GET: /Admin/Shipments/Create?orderId=123
    [HttpGet]
    public async Task<IActionResult> Create(int? orderId)
    {
        if (orderId is > 0)
        {
            // Storefront checkout already creates a Pending shipment. Open it instead
            // of presenting an impossible second-shipment form for the same order.
            var existingId = await _context.Shipments.AsNoTracking()
                .Where(x => x.OrderId == orderId.Value)
                .Select(x => x.Id).FirstOrDefaultAsync();
            if (existingId > 0)
                return RedirectToAction(nameof(Edit), new { id = existingId });
        }
        var model = new ShipmentFormViewModel { OrderId = orderId ?? 0, AllowedStatuses = ["Pending"] };
        await LoadAvailableOrdersAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShipmentFormViewModel model)
    {
        Normalize(model);
        model.AllowedStatuses = ["Pending"];
        ValidateShipment(model);
        if (model.Status != "Pending")
            ModelState.AddModelError(nameof(model.Status), "A new shipment always starts as pending. Edit it to record the next stage.");

        Order? order = null;
        if (model.OrderId > 0)
        {
            order = await _context.Orders.Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.Id == model.OrderId);
            if (order == null)
                ModelState.AddModelError(nameof(model.OrderId), "The selected order does not exist.");
            else if (order.Status is not ("Pending" or "Confirmed" or "Processing"))
                ModelState.AddModelError(nameof(model.OrderId), "Only pending, confirmed or processing orders can receive a new shipment.");
            else if (IsDispatchStatus(model.Status) && RequiresPaymentVerification(order))
                ModelState.AddModelError(nameof(model.Status), "Verify the order payment before dispatching this shipment.");
            else if (await _context.Shipments.AnyAsync(s => s.OrderId == model.OrderId))
                ModelState.AddModelError(nameof(model.OrderId), "This order already has a shipment.");
        }

        if (!ModelState.IsValid || order == null)
        {
            await LoadAvailableOrdersAsync(model);
            return View(model);
        }

        var shipment = new Shipment
        {
            OrderId = order.Id,
            CreatedAt = DateTime.UtcNow
        };
        ApplyForm(shipment, model);
        SetAutomaticDates(shipment);

        _context.Shipments.Add(shipment);
        SyncOrderStatus(order, shipment.Status);

        try
        {
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Shipment created for order {order.OrderNumber}.";
            return RedirectToAction(nameof(Details), new { id = shipment.Id });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create shipment for order {OrderId}", model.OrderId);
            ModelState.AddModelError("", "Could not create shipment. Check whether this order already has one.");
            await LoadAvailableOrdersAsync(model);
            return View(model);
        }
    }

    // GET: /Admin/Shipments/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var shipment = await _context.Shipments
            .AsNoTracking()
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (shipment == null) return NotFound();

        return View(new ShipmentFormViewModel
        {
            Id = shipment.Id,
            OrderId = shipment.OrderId,
            OrderNumber = shipment.Order.OrderNumber,
            CustomerName = shipment.Order.CustomerName,
            Status = shipment.Status,
            CourierName = shipment.CourierName,
            TrackingNumber = shipment.TrackingNumber,
            TrackingUrl = shipment.TrackingUrl,
            EstimatedDeliveryAt = shipment.EstimatedDeliveryAt,
            Notes = shipment.Notes,
            AllowedStatuses = new[] { shipment.Status }.Concat(ShipmentStatuses.AllowedNext(shipment.Status)).Distinct().ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ShipmentFormViewModel model)
    {
        if (model.Id != id) return BadRequest();

        var shipment = await _context.Shipments
            .Include(s => s.Order).ThenInclude(o => o.Payment)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (shipment == null) return NotFound();

        // OrderId is never editable. Ignore posted OrderId entirely.
        model.OrderId = shipment.OrderId;
        ModelState.Remove(nameof(model.OrderId));
        model.OrderNumber = shipment.Order.OrderNumber;
        model.CustomerName = shipment.Order.CustomerName;
        model.AllowedStatuses = new[] { shipment.Status }
            .Concat(ShipmentStatuses.AllowedNext(shipment.Status)).Distinct().ToList();
        Normalize(model);
        ValidateShipment(model);

        if (!ShipmentStatuses.CanTransition(shipment.Status, model.Status))
            ModelState.AddModelError(nameof(model.Status),
                $"Cannot change shipment from {ShipmentStatuses.Label(shipment.Status)} to {ShipmentStatuses.Label(model.Status)}. Move through the next delivery stage.");

        if (model.Status != shipment.Status &&
            (shipment.Order.Status is "Cancelled" or "Returned"))
            ModelState.AddModelError(nameof(model.Status),
                "A cancelled or returned order cannot resume fulfillment.");

        if (model.Status == "Cancelled" && shipment.Order.Status != "Cancelled")
            ModelState.AddModelError(nameof(model.Status),
                "Cancel the order from its order details so inventory and the shipment are updated together.");

        if (model.Status != shipment.Status && IsDispatchStatus(model.Status))
        {
            if (RequiresPaymentVerification(shipment.Order))
                ModelState.AddModelError(nameof(model.Status),
                    "Verify online or advance payment before dispatch. Cash-on-delivery orders can proceed.");
            if (shipment.Order.Status is not ("Processing" or "Shipped" or "Delivered"))
                ModelState.AddModelError(nameof(model.Status),
                    "Move this order to Processing before marking the courier shipment as dispatched.");
        }

        if (shipment.Status == "Delivered" &&
            model.Status != "Delivered" && model.Status != "Returned")
        {
            ModelState.AddModelError(nameof(model.Status),
                "A delivered shipment may only remain delivered or be marked returned.");
        }

        if (!ModelState.IsValid) return View(model);

        var previousStatus = shipment.Status;
        ApplyForm(shipment, model);
        SetAutomaticDates(shipment);

        if (previousStatus != shipment.Status)
            SyncOrderStatus(shipment.Order, shipment.Status);

        try
        {
            await _context.SaveChangesAsync();
            TempData["Success"] = "Shipment updated successfully.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update shipment {ShipmentId}", id);
            ModelState.AddModelError("", "Unable to save the shipment changes. Please try again.");
            return View(model);
        }
    }

    // GET: /Admin/Shipments/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var shipment = await _context.Shipments
            .AsNoTracking()
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == id);
        return shipment == null ? NotFound() : View(shipment);
    }

    // POST: /Admin/Shipments/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var shipment = await _context.Shipments.Include(s => s.Order).FirstOrDefaultAsync(s => s.Id == id);
        if (shipment == null) return NotFound();

        // Checkout creates the sole shipment for each order. Never delete it while
        // the customer still has an active order; this would break tracking.
        if (shipment.Order.Status != "Cancelled" || shipment.Status != "Cancelled")
        {
            TempData["Error"] = "Active and dispatched orders must retain their shipment history. Cancel the eligible order first.";
            return RedirectToAction(nameof(Details), new { id });
        }

        _context.Shipments.Remove(shipment);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Shipment deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadAvailableOrdersAsync(ShipmentFormViewModel model)
    {
        var orders = await _context.Orders.AsNoTracking()
            .Where(o => (o.Status == "Pending" || o.Status == "Confirmed" || o.Status == "Processing") &&
                        !_context.Shipments.Any(s => s.OrderId == o.Id))
            .OrderByDescending(o => o.Id)
            .Take(300)
            .Select(o => new { o.Id, o.OrderNumber, o.CustomerName })
            .ToListAsync();

        model.AvailableOrders = orders.Select(o => new SelectListItem
        {
            Value = o.Id.ToString(),
            Text = $"{o.OrderNumber} — {o.CustomerName}"
        }).ToList();
    }

    private void ValidateShipment(ShipmentFormViewModel model)
    {
        if (!ShipmentStatuses.IsValid(model.Status))
            ModelState.AddModelError(nameof(model.Status), "Invalid shipment status.");

        if (!string.IsNullOrEmpty(model.TrackingUrl) &&
            (!Uri.TryCreate(model.TrackingUrl, UriKind.Absolute, out var url) ||
              (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)))
        {
            ModelState.AddModelError(nameof(model.TrackingUrl), "Use an http:// or https:// tracking URL.");
        }

        if (model.Status is "Shipped" or "InTransit" or "OutForDelivery" or "Delivered")
        {
            if (string.IsNullOrWhiteSpace(model.CourierName))
                ModelState.AddModelError(nameof(model.CourierName), "Enter the courier before dispatching.");
        }
    }

    private static bool IsDispatchStatus(string? status) =>
        status is "Shipped" or "InTransit" or "OutForDelivery" or "Delivered";

    private static bool RequiresPaymentVerification(Order order) =>
        order.Payment == null ||
        (order.Payment.Method is "Bkash" or "ManualAdvance" && order.Payment.Status != "Paid");

    private static void Normalize(ShipmentFormViewModel model)
    {
        model.Status = model.Status?.Trim() ?? "";
        model.CourierName = Clean(model.CourierName);
        model.TrackingNumber = Clean(model.TrackingNumber);
        model.TrackingUrl = Clean(model.TrackingUrl);
        model.Notes = Clean(model.Notes);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ApplyForm(Shipment shipment, ShipmentFormViewModel model)
    {
        shipment.Status = model.Status;
        shipment.CourierName = model.CourierName;
        shipment.TrackingNumber = model.TrackingNumber;
        shipment.TrackingUrl = model.TrackingUrl;
        shipment.EstimatedDeliveryAt = model.EstimatedDeliveryAt?.Date;
        shipment.Notes = model.Notes;
    }

    private static void SetAutomaticDates(Shipment shipment)
    {
        var now = DateTime.UtcNow;
        if (shipment.Status is "Shipped" or "InTransit" or "OutForDelivery" or "Delivered" or "Returned")
            shipment.ShippedAt ??= now;
        if (shipment.Status == "Delivered")
            shipment.DeliveredAt ??= now;
    }

    private void SyncOrderStatus(Order order, string shipmentStatus)
    {
        string? orderStatus = shipmentStatus switch
        {
            "Shipped" or "InTransit" or "OutForDelivery" => "Shipped",
            "Delivered" => "Delivered",
            "Returned" => "Returned",
            _ => null
        };

        if (orderStatus == null || order.Status == orderStatus) return;

        order.Status = orderStatus;
        order.UpdatedAt = DateTime.UtcNow;
        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = orderStatus,
            Note = $"Shipment status updated: {ShipmentStatuses.Label(shipmentStatus)}.",
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedAt = DateTime.UtcNow
        });
    }
}
