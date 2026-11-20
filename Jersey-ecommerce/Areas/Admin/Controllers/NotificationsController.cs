using System.Security.Claims;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Notifications;
using Jersey_ecommerce.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers;

/// <summary>
/// Shared store event history with private read/unread state for each admin.
/// Only role=Admin can list, poll, mark-read, or open notifications.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Notifications")]
public sealed class NotificationsController(
    ApplicationDbContext db, AdminNotificationReader reader) : Controller
{
    private string AdminId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Admin login is required.");

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string category = "All", string status = "all", string? search = null,
        int page = 1, CancellationToken ct = default)
    {
        const int pageSize = 25;
        status = status is "unread" or "read" ? status : "all";
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];

        var availableCategories = await db.AdminNotifications.AsNoTracking()
            .Select(n => n.Category).Distinct().OrderBy(x => x).ToListAsync(ct);
        if (category != "All" && !availableCategories.Contains(category))
            category = "All";

        var query = reader.Rows(AdminId);
        if (category != "All") query = query.Where(x => x.Category == category);
        if (status == "unread") query = query.Where(x => !x.IsRead);
        if (status == "read") query = query.Where(x => x.IsRead);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Title.Contains(search) ||
                x.Message.Contains(search) || x.EventType.Contains(search));

        var total = await query.CountAsync(ct);
        var maxPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, maxPages);

        return View(new AdminNotificationsIndexViewModel
        {
            Category = category,
            Status = status,
            Search = search,
            Page = page,
            PageSize = pageSize,
            Total = total,
            UnreadCount = await reader.UnreadCountAsync(AdminId, ct),
            Categories = availableCategories,
            Items = await query.OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct)
        });
    }

    // Fast AJAX endpoints for the shared topbar: never expose customer/other
    // admin reading state to unauthenticated or non-admin users.
    [HttpGet("Summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Json(new { unread = await reader.UnreadCountAsync(AdminId, ct) });
    }

    [HttpGet("Popup")]
    public async Task<IActionResult> Popup(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return PartialView("~/Areas/Admin/Views/Notifications/_NotificationPopup.cshtml",
            await reader.BellAsync(AdminId, ct));
    }

    [HttpPost("Open/{id:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(long id)
    {
        var notification = await db.AdminNotifications.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id);
        if (notification == null) return NotFound();

        await MarkReadAsync(notification.Id, AdminId);
        // No open redirects. URLs originate from hard-coded, trusted domain
        // events, but validate again before navigating.
        var destination = notification.TargetUrl;
        if (!destination.StartsWith("/Admin/", StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith("//") || destination.Contains('\\'))
            destination = "/Admin/Notifications";
        return LocalRedirect(destination);
    }

    [HttpPost("Read/{id:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Read(long id, string? returnUrl = null)
    {
        if (!await db.AdminNotifications.AsNoTracking().AnyAsync(x => x.Id == id))
            return NotFound();
        await MarkReadAsync(id, AdminId);
        return Redirect(AllowedReturn(returnUrl));
    }

    [HttpPost("Unread/{id:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unread(long id, string? returnUrl = null)
    {
        if (!await db.AdminNotifications.AsNoTracking().AnyAsync(x => x.Id == id))
            return NotFound();
        var reads = await db.AdminNotificationReads
            .Where(x => x.AdminNotificationId == id && x.AdminUserId == AdminId)
            .ToListAsync();
        if (reads.Count > 0)
        {
            db.AdminNotificationReads.RemoveRange(reads);
            await db.SaveChangesAsync();
        }
        return Redirect(AllowedReturn(returnUrl));
    }

    [HttpPost("ReadAll")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAll(string? returnUrl = null)
    {
        // Bounded indexed batches avoid loading years of activity at once.
        // The unique index guarantees at most one receipt per admin/event.
        const int batchSize = 500;
        while (true)
        {
            var missing = await db.AdminNotifications.AsNoTracking()
                .Where(n => !n.Reads.Any(r => r.AdminUserId == AdminId))
                .OrderBy(n => n.Id).Select(n => n.Id)
                .Take(batchSize).ToListAsync();
            if (missing.Count == 0) break;

            db.AdminNotificationReads.AddRange(missing.Select(id =>
                new AdminNotificationRead
                {
                    AdminNotificationId = id,
                    AdminUserId = AdminId,
                    ReadAt = DateTime.UtcNow
                }));
            await db.SaveChangesAsync();
            if (missing.Count < batchSize) break;
        }
        TempData["Success"] = "Your notifications have been marked as read.";
        return Redirect(AllowedReturn(returnUrl));
    }

    private async Task MarkReadAsync(long notificationId, string adminId)
    {
        if (await db.AdminNotificationReads.AsNoTracking().AnyAsync(
            r => r.AdminNotificationId == notificationId && r.AdminUserId == adminId))
            return;

        var receipt = new AdminNotificationRead
        {
            AdminNotificationId = notificationId,
            AdminUserId = adminId,
            ReadAt = DateTime.UtcNow
        };
        db.AdminNotificationReads.Add(receipt);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two browser tabs may acknowledge the same notification at once.
            // A unique-index collision is safe only if the desired row exists.
            db.Entry(receipt).State = EntityState.Detached;
            if (!await db.AdminNotificationReads.AsNoTracking().AnyAsync(
                r => r.AdminNotificationId == notificationId && r.AdminUserId == adminId))
                throw;
        }
    }

    private static string AllowedReturn(string? requested) =>
        !string.IsNullOrWhiteSpace(requested) &&
        requested.StartsWith("/Admin/Notifications", StringComparison.OrdinalIgnoreCase) &&
        !requested.StartsWith("//") && !requested.Contains('\\')
            ? requested
            : "/Admin/Notifications";
}
