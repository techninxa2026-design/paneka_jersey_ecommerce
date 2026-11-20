using System.Text.RegularExpressions;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class BrandsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BrandsController> _logger;

        private const long MaxLogoBytes = 5 * 1024 * 1024;

        public BrandsController(
            ApplicationDbContext db,
            IWebHostEnvironment env,
            ILogger<BrandsController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // GET /admin/brands
        [HttpGet]
        public async Task<IActionResult> Index(string? search, string? status)
        {
            status = status is "active" or "inactive" ? status : "all";

            var all = _db.Brands.AsNoTracking();
            var filtered = all;

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                filtered = filtered.Where(b =>
                    b.Name.Contains(search) || b.Slug.Contains(search));
            }

            if (status == "active") filtered = filtered.Where(b => b.IsActive);
            if (status == "inactive") filtered = filtered.Where(b => !b.IsActive);

            var model = new BrandIndexViewModel
            {
                Search = search,
                Status = status,
                TotalBrands = await all.CountAsync(),
                ActiveBrands = await all.CountAsync(b => b.IsActive),
                ProductBrandCount = await all.CountAsync(b => b.Products.Any()),
                Brands = await filtered
                    .OrderBy(b => b.Name)
                    .Select(b => new BrandListItemViewModel
                    {
                        Id = b.Id,
                        Name = b.Name,
                        Slug = b.Slug,
                        LogoUrl = b.LogoUrl,
                        IsActive = b.IsActive,
                        ProductCount = b.Products.Count
                    })
                    .ToListAsync()
            };

            return View(model);
        }

        // GET /admin/brands/create
        [HttpGet]
        public IActionResult Create() => View(new BrandFormViewModel { IsActive = true });

        // POST /admin/brands/create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> Create(BrandFormViewModel model)
        {
            NormalizeAndValidate(model);
            await ValidateUniqueSlugAsync(model.Slug!, exceptId: null);

            if (!ModelState.IsValid)
                return View(model);

            string? newlySavedLogo = null;

            try
            {
                if (model.LogoFile != null)
                    newlySavedLogo = await SaveLogoAsync(model.LogoFile);

                var brand = new Brand
                {
                    Name = model.Name,
                    Slug = model.Slug!,
                    LogoUrl = newlySavedLogo,
                    IsActive = model.IsActive
                };

                _db.Brands.Add(brand);
                await _db.SaveChangesAsync();

                TempData["Success"] = "Brand created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Failed to create brand {BrandName}", model.Name);
                DeleteLogoQuietly(newlySavedLogo);
                ModelState.AddModelError("", _env.IsDevelopment()
                    ? ex.GetBaseException().Message
                    : "The brand could not be saved. Check the server log.");
                return View(model);
            }
        }

        // GET /admin/brands/edit/{id}
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var brand = await _db.Brands.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);
            if (brand == null) return NotFound();

            return View(new BrandFormViewModel
            {
                Id = brand.Id,
                Name = brand.Name,
                Slug = brand.Slug,
                LogoUrl = brand.LogoUrl,
                IsActive = brand.IsActive
            });
        }

        // POST /admin/brands/edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> Edit(int id, BrandFormViewModel model)
        {
            if (id != model.Id) return NotFound();

            var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id);
            if (brand == null) return NotFound();

            // Do not trust a hidden LogoUrl field submitted by the browser.
            model.LogoUrl = brand.LogoUrl;
            NormalizeAndValidate(model);
            await ValidateUniqueSlugAsync(model.Slug!, exceptId: id);

            if (model.LogoFile != null && model.RemoveLogo)
                ModelState.AddModelError(nameof(model.RemoveLogo),
                    "Choose either a replacement logo or Remove current logo, not both.");

            if (!ModelState.IsValid)
                return View(model);

            string? newlySavedLogo = null;
            var oldLogo = brand.LogoUrl;

            try
            {
                if (model.LogoFile != null)
                    newlySavedLogo = await SaveLogoAsync(model.LogoFile);

                brand.Name = model.Name;
                brand.Slug = model.Slug!;
                brand.IsActive = model.IsActive;

                if (newlySavedLogo != null)
                    brand.LogoUrl = newlySavedLogo;
                else if (model.RemoveLogo)
                    brand.LogoUrl = null;

                await _db.SaveChangesAsync();

                // Only delete the old file after the database save succeeds.
                if ((newlySavedLogo != null || model.RemoveLogo) && oldLogo != null)
                    DeleteLogoQuietly(oldLogo);

                TempData["Success"] = "Brand updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Failed to update brand {BrandId}", id);
                DeleteLogoQuietly(newlySavedLogo);
                ModelState.AddModelError("", _env.IsDevelopment()
                    ? ex.GetBaseException().Message
                    : "The brand could not be updated. Check the server log.");
                return View(model);
            }
        }

        // GET /admin/brands/delete/{id}: explicit confirmation page.
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var model = await _db.Brands.AsNoTracking()
                .Where(b => b.Id == id)
                .Select(b => new BrandDeleteViewModel
                {
                    Id = b.Id,
                    Name = b.Name,
                    Slug = b.Slug,
                    LogoUrl = b.LogoUrl,
                    ProductCount = b.Products.Count
                })
                .FirstOrDefaultAsync();

            return model == null ? NotFound() : View(model);
        }

        // POST /admin/brands/delete/{id}: rechecks dependencies.
        [HttpPost]
        [ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id);
            if (brand == null)
            {
                TempData["Error"] = "Brand not found.";
                return RedirectToAction(nameof(Index));
            }

            if (await _db.Products.AnyAsync(p => p.BrandId == id))
            {
                TempData["Error"] = "Cannot delete this brand while products use it. Reassign those products first.";
                return RedirectToAction(nameof(Index));
            }

            var oldLogo = brand.LogoUrl;

            try
            {
                _db.Brands.Remove(brand);
                await _db.SaveChangesAsync();
                DeleteLogoQuietly(oldLogo);
                TempData["Success"] = "Brand deleted successfully.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to delete brand {BrandId}", id);
                TempData["Error"] = "This brand is referenced by other data and cannot be deleted.";
            }

            return RedirectToAction(nameof(Index));
        }

        private void NormalizeAndValidate(BrandFormViewModel model)
        {
            model.Name = (model.Name ?? "").Trim();

            var slugSource = string.IsNullOrWhiteSpace(model.Slug)
                ? model.Name : model.Slug;
            model.Slug = GenerateSlug(slugSource);

            // Slug is generated/normalized by the server, not trusted from POST.
            ModelState.Remove(nameof(model.Slug));

            if (string.IsNullOrWhiteSpace(model.Name))
                ModelState.AddModelError(nameof(model.Name), "Brand name is required.");

            if (string.IsNullOrWhiteSpace(model.Slug))
                ModelState.AddModelError(nameof(model.Slug), "Enter a valid name or URL slug using letters/numbers.");

            if (model.LogoFile is { } file)
            {
                var logoError = ValidateLogo(file);
                if (logoError != null)
                    ModelState.AddModelError(nameof(model.LogoFile), logoError);
            }
        }

        private async Task ValidateUniqueSlugAsync(string slug, int? exceptId)
        {
            if (string.IsNullOrWhiteSpace(slug)) return;

            var exists = await _db.Brands.AnyAsync(b =>
                b.Slug == slug && (!exceptId.HasValue || b.Id != exceptId.Value));
            if (exists)
                ModelState.AddModelError(nameof(BrandFormViewModel.Slug),
                    "This brand slug already exists. Choose another one.");
        }

        private static string GenerateSlug(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var slug = Regex.Replace(text.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            return slug.Length <= 120 ? slug : slug[..120].TrimEnd('-');
        }

        private static string? ValidateLogo(IFormFile logo)
        {
            if (logo.Length == 0) return "Choose a non-empty image.";
            if (logo.Length > MaxLogoBytes) return "Logo size cannot exceed 5 MB.";

            var extension = Path.GetExtension(logo.FileName).ToLowerInvariant();
            var contentType = logo.ContentType.ToLowerInvariant();
            var valid = extension switch
            {
                ".jpg" or ".jpeg" => contentType == "image/jpeg",
                ".png" => contentType == "image/png",
                ".webp" => contentType == "image/webp",
                _ => false
            };
            return valid ? null : "Only JPG, PNG, or WEBP images are supported.";
        }

        private string UploadDirectory()
        {
            var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            return Path.Combine(root, "uploads", "brands");
        }

        private async Task<string> SaveLogoAsync(IFormFile logo)
        {
            var folder = UploadDirectory();
            Directory.CreateDirectory(folder);
            var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(folder, fileName);

            try
            {
                await using var stream = new FileStream(
                    fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await logo.CopyToAsync(stream);
            }
            catch
            {
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                throw;
            }

            return $"/uploads/brands/{fileName}";
        }

        private void DeleteLogoQuietly(string? logoUrl)
        {
            if (string.IsNullOrWhiteSpace(logoUrl) ||
                !logoUrl.StartsWith("/uploads/brands/", StringComparison.OrdinalIgnoreCase))
                return;

            var filename = Path.GetFileName(logoUrl);
            if (string.IsNullOrWhiteSpace(filename)) return;

            var path = Path.Combine(UploadDirectory(), filename);
            try
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to delete brand logo {Path}", path);
            }
        }
    }
}
