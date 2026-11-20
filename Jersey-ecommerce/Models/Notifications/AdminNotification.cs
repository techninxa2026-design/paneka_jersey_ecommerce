using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Notifications;

/// <summary>
/// Durable, store-wide activity feed visible only to authenticated Admin users.
/// Only business identifiers and concise descriptions are stored: no credentials,
/// PINs, OTPs, passwords, or customer payment secrets.
/// </summary>
public sealed class AdminNotification
{
    public long Id { get; set; }

    [Required, StringLength(32)]
    public string Category { get; set; } = "System";

    [Required, StringLength(64)]
    public string EventType { get; set; } = string.Empty;

    [Required, StringLength(16)]
    public string Severity { get; set; } = "Info";

    [Required, StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Message { get; set; } = string.Empty;

    [Required, StringLength(450)]
    public string TargetUrl { get; set; } = "/Admin/Dashboard";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AdminNotificationRead> Reads { get; set; }
        = new List<AdminNotificationRead>();
}

/// <summary>
/// Per-administrator read receipts. Reading a message never hides it from
/// a different administrator and never destroys the shared audit history.
/// </summary>
public sealed class AdminNotificationRead
{
    public long Id { get; set; }

    public long AdminNotificationId { get; set; }

    [Required, StringLength(450)]
    public string AdminUserId { get; set; } = string.Empty;

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;

    public AdminNotification Notification { get; set; } = null!;
}
