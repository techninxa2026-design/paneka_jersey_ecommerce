using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Shopping;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/storefront/cart")]
public sealed class StorefrontCartApiController(CheckoutService cart, ApplicationDbContext db) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public sealed record AddRequest(int ProductId, int VariantId, int Quantity);
    public sealed record QuantityRequest(int Quantity);
    public sealed record CouponRequest(string? Code);
    public sealed record WishRequest(int ProductId);

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await cart.GetCart(UserId));

    [HttpGet("count")]
    public async Task<IActionResult> Count() => Ok(new { count = (await cart.GetCart(UserId)).ItemCount });

    [AllowAnonymous]
    [HttpGet("variants/{productId:int}")]
    public async Task<IActionResult> Variants(int productId) => Ok(await cart.Variants(productId));

    [HttpGet("recommendations")]
    public async Task<IActionResult> Recommendations() => Ok(await cart.Recommendations(UserId));

    [HttpPost("items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([FromBody] AddRequest body)
    {
        try
        {
            await cart.Add(UserId, body.ProductId, body.VariantId, body.Quantity);
            return Ok(await cart.GetCart(UserId));
        }
        catch (CheckoutException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "Your bag changed. Refresh and try again." }); }
    }

    [HttpPatch("items/{itemId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeQuantity(int itemId, [FromBody] QuantityRequest body)
    {
        try { await cart.SetQuantity(UserId, itemId, body.Quantity); return Ok(await cart.GetCart(UserId)); }
        catch (CheckoutException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("items/{itemId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int itemId)
    {
        try { await cart.Remove(UserId, itemId); return Ok(await cart.GetCart(UserId)); }
        catch (CheckoutException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("coupons")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Coupon([FromBody] CouponRequest body)
    {
        try { await cart.SetCoupon(UserId, body.Code); return Ok(await cart.GetCart(UserId)); }
        catch (CheckoutException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("wishlist")]
    public async Task<IActionResult> Wishlist()
    {
        var ids = await db.WishListItems.AsNoTracking()
            .Where(i => i.Wishlist.UserId == UserId).Select(i => i.ProductId).ToListAsync();
        return Ok(new { productIds = ids });
    }

    [HttpDelete("wishlist/{productId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveWish(int productId)
    {
        var item = await db.WishListItems
            .SingleOrDefaultAsync(i => i.Wishlist.UserId == UserId && i.ProductId == productId);
        if (item is not null) { db.WishListItems.Remove(item); await db.SaveChangesAsync(); }
        return Ok(new { saved = false });
    }

    [HttpPost("wishlist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWish([FromBody] WishRequest body)
    {
        if (!await db.Products.AnyAsync(p => p.Id == body.ProductId && p.IsPublished))
            return BadRequest(new { message = "Product not found." });
        var list = await db.WishLists.SingleOrDefaultAsync(w => w.UserId == UserId);
        if (list is null)
        {
            list = new WishList { UserId = UserId };
            db.WishLists.Add(list);
            await db.SaveChangesAsync();
        }
        if (!await db.WishListItems.AnyAsync(i => i.WishlistId == list.Id && i.ProductId == body.ProductId))
        {
            db.WishListItems.Add(new WishListItem { WishlistId = list.Id, ProductId = body.ProductId });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return Conflict(new { message = "Wishlist changed; refresh and try again." }); }
        }
        return Ok(new { saved = true });
    }
}
