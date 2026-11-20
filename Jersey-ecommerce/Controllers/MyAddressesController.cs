using System.Security.Claims;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Controllers;

[Authorize, Route("MyAddresses")]
public sealed class MyAddressesController(ApplicationDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await db.Addresses.AsNoTracking()
        .Where(x => x.UserId == UserId).OrderByDescending(x => x.IsDefault)
        .ThenByDescending(x => x.CreatedAt).ToListAsync());

    [HttpPost("Save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Address input)
    {
        // Bind explicitly: caller may not set UserId, IsDefault or CreatedAt.
        if (string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Length > 150 ||
            string.IsNullOrWhiteSpace(input.PhoneNumber) || input.PhoneNumber.Length > 30 ||
            string.IsNullOrWhiteSpace(input.AddressLine1) || input.AddressLine1.Length > 200 ||
            string.IsNullOrWhiteSpace(input.City) || input.City.Length > 100 ||
            input.AddressLine2?.Length > 200 || input.District?.Length > 100 ||
            input.PostalCode?.Length > 20)
        {
            TempData["AddressInfo"] = "Enter a valid name, phone, street address and city.";
            return RedirectToAction(nameof(Index));
        }
        var existing = input.Id > 0
            ? await db.Addresses.SingleOrDefaultAsync(x => x.Id == input.Id && x.UserId == UserId)
            : null;
        if (input.Id > 0 && existing == null) return NotFound();
        if (existing == null)
        {
            bool first = !await db.Addresses.AnyAsync(x => x.UserId == UserId);
            existing = new Address { UserId = UserId, IsDefault = first, Country = "Bangladesh" };
            db.Addresses.Add(existing);
        }
        existing.FullName = input.FullName.Trim();
        existing.PhoneNumber = input.PhoneNumber.Trim();
        existing.AddressLine1 = input.AddressLine1.Trim();
        existing.AddressLine2 = input.AddressLine2?.Trim();
        existing.City = input.City.Trim();
        existing.District = input.District?.Trim();
        existing.PostalCode = input.PostalCode?.Trim();
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Default/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Default(int id)
    {
        var own = await db.Addresses.Where(x => x.UserId == UserId).ToListAsync();
        if (!own.Any(x => x.Id == id)) return NotFound();
        foreach (var address in own) address.IsDefault = address.Id == id;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Delete/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var address = await db.Addresses.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (address == null) return NotFound();
        var wasDefault = address.IsDefault;
        db.Addresses.Remove(address);
        await db.SaveChangesAsync();
        if (wasDefault)
        {
            var next = await db.Addresses.Where(x => x.UserId == UserId).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
            if (next != null) { next.IsDefault = true; await db.SaveChangesAsync(); }
        }
        return RedirectToAction(nameof(Index));
    }
}
