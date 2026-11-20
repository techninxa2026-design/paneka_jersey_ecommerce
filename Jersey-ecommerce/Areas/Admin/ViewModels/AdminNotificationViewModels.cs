namespace Jersey_ecommerce.Areas.Admin.ViewModels;

public sealed class AdminNotificationRow
{
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string TargetUrl { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class AdminNotificationBellViewModel
{
    public int UnreadCount { get; set; }
    public List<AdminNotificationRow> Items { get; set; } = new();
}

public sealed class AdminNotificationsIndexViewModel
{
    public int Page { get; set; }
    public int PageSize { get; set; } = 25;
    public int Total { get; set; }
    public int UnreadCount { get; set; }
    public string Category { get; set; } = "All";
    public string Status { get; set; } = "all";
    public string? Search { get; set; }
    public List<string> Categories { get; set; } = new();
    public List<AdminNotificationRow> Items { get; set; } = new();
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Total / PageSize));
}
