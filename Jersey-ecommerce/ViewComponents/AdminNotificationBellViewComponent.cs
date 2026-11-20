using System.Security.Claims;
using Jersey_ecommerce.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jersey_ecommerce.ViewComponents;

/// <summary>The bell is embedded on every admin page and shares per-admin state.</summary>
public sealed class AdminNotificationBellViewComponent(AdminNotificationReader reader) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal?.IsInRole("Admin") != true)
            return Content(string.Empty);
        var adminId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminId))
            return Content(string.Empty);
        return View(await reader.BellAsync(adminId));
    }
}
