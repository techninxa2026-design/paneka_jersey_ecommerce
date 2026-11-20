using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jersey_ecommerce.Controllers;

[Authorize]
[Route("Cart")]
public sealed class CartController : Controller
{
    [HttpGet("/cart")]
    public IActionResult Index() => View();
}
