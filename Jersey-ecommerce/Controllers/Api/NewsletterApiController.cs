using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers.Api;

[ApiController, AllowAnonymous, Route("api/storefront/newsletter")]
public sealed class NewsletterApiController(ApplicationDbContext db) : ControllerBase
{
    public sealed record SubscribeRequest(string? Email);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "Enter a valid email address." });
        var existing = await db.NewsletterSubscribers.SingleOrDefaultAsync(x => x.Email == email);
        if (existing == null) db.NewsletterSubscribers.Add(new NewsletterSubscriber { Email = email });
        else { existing.IsSubscribed = true; existing.UnsubscribedAt = null; }
        await db.SaveChangesAsync();
        return Ok(new { message = "You're subscribed to PANENKA updates." });
    }
}
