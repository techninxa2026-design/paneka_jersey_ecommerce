using Jersey_ecommerce.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers;

public sealed class HomeController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Privacy() => View();

    // The production exception middleware points to /Home/Error.
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        ViewData["RequestId"] = HttpContext.TraceIdentifier;
        return View();
    }

    // Public support details are controlled from Admin > Settings.
    [HttpGet]
    public async Task<IActionResult> Contact()
    {
        var keys = new[] { "contact.email", "contact.phone", "contact.address", "contact.whatsapp", "social.instagram", "general.store_name" };
        var publicSettings = await db.SiteSettings.AsNoTracking()
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, x => x.Value);
        return View(publicSettings);
    }
}
