using System;
using System.Linq;
using System.Threading.Tasks;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Promotions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    [Route("admin/coupons")]
    public class CouponsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CouponsController> _logger;

        public CouponsController(ApplicationDbContext context, ILogger<CouponsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET /admin/coupons
        [HttpGet("")]
        public async Task<IActionResult> Index(string? search, string status = "all")
        {
            var now = DateTime.UtcNow;
            var coupons = await _context.Coupons
                .AsNoTracking()
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var rows = coupons.Select(c => new CouponRowViewModel
            {
                Coupon = c,
                Status = GetCouponStatus(c, now)
            }).ToList();

            var model = new CouponsIndexViewModel
            {
                Search = search?.Trim(),
                Status = status?.Trim().ToLowerInvariant() ?? "all",
                Total = rows.Count,
                Active = rows.Count(r => r.Status == "active"),
                Scheduled = rows.Count(r => r.Status == "scheduled"),
                Expired = rows.Count(r => r.Status == "expired"),
                Exhausted = rows.Count(r => r.Status == "exhausted"),
                Inactive = rows.Count(r => r.Status == "inactive"),
                TotalRedemptions = coupons.Sum(c => c.UsageCount)
            };

            if (!string.IsNullOrWhiteSpace(model.Search))
            {
                rows = rows.Where(r =>
                    r.Coupon.Code.Contains(model.Search, StringComparison.OrdinalIgnoreCase) ||
                    (r.Coupon.Description?.Contains(model.Search, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
            }

            if (model.Status != "all")
                rows = rows.Where(r => r.Status == model.Status).ToList();

            model.Coupons = rows;
            return View(model);
        }

        // GET /admin/coupons/create
        [HttpGet("create")]
        public IActionResult Create() => View(new CouponFormViewModel());

        // POST /admin/coupons/create
        [HttpPost("create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CouponFormViewModel model)
        {
            NormalizeAndValidate(model);
            if (!ModelState.IsValid) return View(model);

            if (await _context.Coupons.AnyAsync(c => c.Code == model.Code))
            {
                ModelState.AddModelError(nameof(model.Code), "This coupon code already exists.");
                return View(model);
            }

            var coupon = new Coupon
            {
                Code = model.Code,
                Description = model.Description,
                DiscountType = model.DiscountType,
                DiscountValue = model.DiscountValue,
                MinimumOrderAmount = model.MinimumOrderAmount,
                MaximumDiscountAmount = model.MaximumDiscountAmount,
                UsageLimit = model.UsageLimit,
                UsageLimitPerUser = model.UsageLimitPerUser,
                UsageCount = 0,
                StartsAt = SpecifyUtc(model.StartsAt),
                ExpiresAt = SpecifyUtc(model.ExpiresAt),
                IsActive = model.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Coupons.Add(coupon);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error creating coupon {Code}", model.Code);
                ModelState.AddModelError(nameof(model.Code),
                    "Could not save this coupon. Check that the code is unique.");
                return View(model);
            }

            TempData["Success"] = $"Coupon {coupon.Code} created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET /admin/coupons/edit/5
        [HttpGet("edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var coupon = await _context.Coupons.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);
            if (coupon is null) return NotFound();

            return View(new CouponFormViewModel
            {
                Id = coupon.Id,
                Code = coupon.Code,
                Description = coupon.Description,
                DiscountType = coupon.DiscountType,
                DiscountValue = coupon.DiscountValue,
                MinimumOrderAmount = coupon.MinimumOrderAmount,
                MaximumDiscountAmount = coupon.MaximumDiscountAmount,
                UsageLimit = coupon.UsageLimit,
                UsageLimitPerUser = coupon.UsageLimitPerUser,
                StartsAt = coupon.StartsAt,
                ExpiresAt = coupon.ExpiresAt,
                IsActive = coupon.IsActive
            });
        }

        // POST /admin/coupons/edit/5
        [HttpPost("edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CouponFormViewModel model)
        {
            if (id != model.Id) return BadRequest();

            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == id);
            if (coupon is null) return NotFound();

            NormalizeAndValidate(model, coupon.UsageCount);
            if (!ModelState.IsValid) return View(model);

            if (await _context.Coupons.AnyAsync(c => c.Code == model.Code && c.Id != id))
            {
                ModelState.AddModelError(nameof(model.Code), "This coupon code already exists.");
                return View(model);
            }

            // Explicit mapping keeps UsageCount, CreatedAt and usage history immutable here.
            coupon.Code = model.Code;
            coupon.Description = model.Description;
            coupon.DiscountType = model.DiscountType;
            coupon.DiscountValue = model.DiscountValue;
            coupon.MinimumOrderAmount = model.MinimumOrderAmount;
            coupon.MaximumDiscountAmount = model.MaximumDiscountAmount;
            coupon.UsageLimit = model.UsageLimit;
            coupon.UsageLimitPerUser = model.UsageLimitPerUser;
            coupon.StartsAt = SpecifyUtc(model.StartsAt);
            coupon.ExpiresAt = SpecifyUtc(model.ExpiresAt);
            coupon.IsActive = model.IsActive;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error updating coupon {Id}", id);
                ModelState.AddModelError(string.Empty,
                    "Coupon could not be updated. Please try again.");
                return View(model);
            }

            TempData["Success"] = $"Coupon {coupon.Code} updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // POST /admin/coupons/toggle/5
        [HttpPost("toggle/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var coupon = await _context.Coupons.FindAsync(id);
            if (coupon is null) return NotFound();

            coupon.IsActive = !coupon.IsActive;
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Coupon {coupon.Code} {(coupon.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(Index));
        }

        // POST /admin/coupons/delete/5
        [HttpPost("delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var coupon = await _context.Coupons.FindAsync(id);
            if (coupon is null) return NotFound();

            if (coupon.UsageCount > 0 ||
                await _context.CouponUsages.AnyAsync(u => u.CouponId == id))
            {
                TempData["Error"] = "This coupon has been used. Deactivate it to keep order history intact.";
                return RedirectToAction(nameof(Index));
            }

            _context.Coupons.Remove(coupon);
            try
            {
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Coupon {coupon.Code} deleted.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Unable to delete coupon {Id}", id);
                TempData["Error"] = "This coupon could not be deleted because it is referenced by another record.";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET /admin/coupons/usage/5
        [HttpGet("usage/{id:int}")]
        public async Task<IActionResult> Usage(int id)
        {
            var coupon = await _context.Coupons.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);
            if (coupon is null) return NotFound();

            var usages = await _context.CouponUsages.AsNoTracking()
                .Include(u => u.User)
                .Where(u => u.CouponId == id)
                .OrderByDescending(u => u.UsedAt)
                .ToListAsync();

            return View(new CouponUsageViewModel { Coupon = coupon, Usages = usages });
        }

        // DateTime-local inputs on this admin page represent UTC wall-clock time.
        private static DateTime? SpecifyUtc(DateTime? value) =>
            value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

        private void NormalizeAndValidate(CouponFormViewModel model, int existingUses = 0)
        {
            model.Code = (model.Code ?? string.Empty).Trim().ToUpperInvariant();
            model.Description = string.IsNullOrWhiteSpace(model.Description)
                ? null
                : model.Description.Trim();

            if (model.DiscountType != "Percentage" && model.DiscountType != "Fixed")
                ModelState.AddModelError(nameof(model.DiscountType), "Choose a valid discount type.");

            if (model.DiscountType == "Percentage" && model.DiscountValue > 100m)
                ModelState.AddModelError(nameof(model.DiscountValue),
                    "A percentage discount cannot exceed 100%. ");

            if (model.DiscountType == "Fixed" && model.MaximumDiscountAmount.HasValue)
                ModelState.AddModelError(nameof(model.MaximumDiscountAmount),
                    "Maximum discount is only available for percentage coupons.");

            if (model.StartsAt.HasValue && model.ExpiresAt.HasValue &&
                model.ExpiresAt.Value <= model.StartsAt.Value)
                ModelState.AddModelError(nameof(model.ExpiresAt),
                    "Expiry must be later than the start date.");

            if (model.UsageLimit.HasValue && model.UsageLimit.Value < existingUses)
                ModelState.AddModelError(nameof(model.UsageLimit),
                    $"The total limit cannot be less than {existingUses} existing uses.");

            if (model.UsageLimit.HasValue && model.UsageLimitPerUser.HasValue &&
                model.UsageLimitPerUser.Value > model.UsageLimit.Value)
                ModelState.AddModelError(nameof(model.UsageLimitPerUser),
                    "Per-user limit cannot exceed the total usage limit.");
        }

        private static string GetCouponStatus(Coupon c, DateTime utcNow)
        {
            if (!c.IsActive) return "inactive";
            if (c.StartsAt.HasValue && c.StartsAt.Value > utcNow) return "scheduled";
            if (c.ExpiresAt.HasValue && c.ExpiresAt.Value <= utcNow) return "expired";
            if (c.UsageLimit.HasValue && c.UsageCount >= c.UsageLimit.Value) return "exhausted";
            return "active";
        }
    }
}
