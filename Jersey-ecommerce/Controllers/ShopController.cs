using Microsoft.AspNetCore.Mvc;
namespace Jersey_ecommerce.Controllers;
[Route("shop")]
public sealed class ShopController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
