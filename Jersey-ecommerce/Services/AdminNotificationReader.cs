using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Services;

/// <summary>Indexed, user-specific reads of the shared admin activity journal.</summary>
public sealed class AdminNotificationReader(ApplicationDbContext db)
{
    public IQueryable<AdminNotificationRow> Rows(string adminId) =>
        db.AdminNotifications.AsNoTracking().Select(n => new AdminNotificationRow
        {
            Id = n.Id,
            Category = n.Category,
            EventType = n.EventType,
            Severity = n.Severity,
            Title = n.Title,
            Message = n.Message,
            TargetUrl = n.TargetUrl,
            CreatedAt = n.CreatedAt,
            IsRead = n.Reads.Any(r => r.AdminUserId == adminId)
        });

    public Task<int> UnreadCountAsync(string adminId, CancellationToken ct = default) =>
        db.AdminNotifications.AsNoTracking()
            .CountAsync(n => !n.Reads.Any(r => r.AdminUserId == adminId), ct);

    public async Task<AdminNotificationBellViewModel> BellAsync(
        string adminId, CancellationToken ct = default)
    {
        var count = await UnreadCountAsync(adminId, ct);
        var items = await Rows(adminId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(7).ToListAsync(ct);
        return new AdminNotificationBellViewModel
        {
            UnreadCount = count,
            Items = items
        };
    }
}
