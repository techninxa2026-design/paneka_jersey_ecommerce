using System.Text.RegularExpressions;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class CollectionsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<CollectionsController> _logger;
        private const long MaxImageBytes = 5 * 1024 * 1024;

        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".webp" };

        public CollectionsController(
            ApplicationDbContext db,
            IWebHostEnvironment env,
            ILogger<CollectionsController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // GET /admin/collections
        [HttpGet]
        public async Task<IActionResult> Index(string? search, string? status)
        {
            status = status is "active" or "inactive" ? status : "all";
            var all = _db.Collections.AsNoTracking();
            var query = all;

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(c =>
                    c.Name.Contains(search) || c.Slug.Contains(search) ||
                    (c.Description != null && c.Description.Contains(search)));
            }

            if (status == "active") query = query.Where(c => c.IsActive);
            if (status == "inactive") query = query.Where(c => !c.IsActive);

            var model = new CollectionIndexViewModel
            {
                Search = search,
                Status = status,
                Total = await all.CountAsync(),
                Active = await all.CountAsync(c => c.IsActive),
                WithProducts = await all.CountAsync(c => c.ProductCollections.Any()),
                Items = await query
                    .OrderBy(c => c.DisplayOrder)
                    .ThenBy(c => c.Name)
                    .Select(c => new CollectionListItemViewModel
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Slug = c.Slug,
                        ImageUrl = c.ImageUrl,
                        DisplayOrder = c.DisplayOrder,
                        IsActive = c.IsActive,
                        ProductCount = c.ProductCollections.Count,
                        CreatedAt = c.CreatedAt
                    })
                    .ToListAsync()
            };

            return View(model);
        }

        [HttpGet]
        public IActionResult Create() => View(new CollectionFormViewModel
        {
            IsActive = true,
            DisplayOrder = 0
        });

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> Create(CollectionFormViewModel model)
        {
            NormalizeAndValidate(model);
            model.Slug = await CatalogSlug.UniqueAsync(model.Name, "collection", 120,
                candidate => _db.Collections.AnyAsync(x => x.Slug == candidate));
            await CheckSlugAvailableAsync(model.Slug, null);

            if (!ModelState.IsValid) return View(model);

            string? savedImage = null;
            try
            {
                if (model.ImageFile != null)
                    savedImage = await SaveImageAsync(model.ImageFile);

                var collection = new Collection
                {
                    Name = model.Name,
                    Slug = model.Slug!,
                    Description = model.Description,
                    ImageUrl = savedImage,
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Collections.Add(collection);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Collection created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Create collection failed for {Name}", model.Name);
                DeleteImageQuietly(savedImage);
                ModelState.AddModelError("", _env.IsDevelopment()
                    ? ex.GetBaseException().Message
                    : "Could not create the collection. Check application logs.");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var c = await _db.Collections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();

            return View(new CollectionFormViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                Description = c.Description,
                ImageUrl = c.ImageUrl,
                DisplayOrder = c.DisplayOrder,
                IsActive = c.IsActive
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> Edit(int id, CollectionFormViewModel model)
        {
            if (id != model.Id) return NotFound();
            var collection = await _db.Collections.FirstOrDefaultAsync(x => x.Id == id);
            if (collection == null) return NotFound();

            model.ImageUrl = collection.ImageUrl;
            NormalizeAndValidate(model);
            model.Slug = string.Equals(collection.Name, model.Name, StringComparison.OrdinalIgnoreCase)
                ? collection.Slug
                : await CatalogSlug.UniqueAsync(model.Name, "collection", 120,
                    candidate => _db.Collections.AnyAsync(x => x.Slug == candidate && x.Id != id));
            await CheckSlugAvailableAsync(model.Slug, id);

            if (model.RemoveImage && model.ImageFile != null)
                ModelState.AddModelError(nameof(model.ImageFile),
                    "Choose either Remove current image or a replacement, not both.");

            if (!ModelState.IsValid) return View(model);

            string? savedImage = null;
            string? oldImage = collection.ImageUrl;

            try
            {
                if (model.ImageFile != null)
                    savedImage = await SaveImageAsync(model.ImageFile);

                collection.Name = model.Name;
                collection.Slug = model.Slug!;
                collection.Description = model.Description;
                collection.DisplayOrder = model.DisplayOrder;
                collection.IsActive = model.IsActive;

                if (savedImage != null)
                    collection.ImageUrl = savedImage;
                else if (model.RemoveImage)
                    collection.ImageUrl = null;

                await _db.SaveChangesAsync();

                if (savedImage != null || model.RemoveImage)
                    DeleteImageQuietly(oldImage);

                TempData["Success"] = "Collection updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Edit collection {Id} failed", id);
                DeleteImageQuietly(savedImage);
                ModelState.AddModelError("", _env.IsDevelopment()
                    ? ex.GetBaseException().Message
                    : "Could not update the collection. Check application logs.");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var c = await _db.Collections.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new CollectionListItemViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug,
                    ImageUrl = x.ImageUrl,
                    ProductCount = x.ProductCollections.Count
                }).FirstOrDefaultAsync();
            return c == null ? NotFound() : View(c);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var c = await _db.Collections.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null)
            {
                TempData["Error"] = "Collection not found.";
                return RedirectToAction(nameof(Index));
            }

            var oldImage = c.ImageUrl;
            try
            {
                // ProductCollection join records cascade when a collection is deleted.
                // Products themselves are NOT deleted.
                _db.Collections.Remove(c);
                await _db.SaveChangesAsync();
                DeleteImageQuietly(oldImage);
                TempData["Success"] = "Collection deleted. Its products were kept.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Delete collection {Id} failed", id);
                TempData["Error"] = "Collection could not be deleted because it is referenced elsewhere.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ManageProducts(int id)
        {
            var collection = await _db.Collections.AsNoTracking()
                .Include(c => c.ProductCollections)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (collection == null) return NotFound();

            var selected = collection.ProductCollections.Select(pc => pc.ProductId).ToHashSet();
            var products = await _db.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => new CollectionProductOptionViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    Slug = p.Slug
                })
                .ToListAsync();

            foreach (var p in products) p.Selected = selected.Contains(p.Id);

            return View(new CollectionProductsViewModel
            {
                CollectionId = id,
                CollectionName = collection.Name,
                SelectedProductIds = selected.ToList(),
                Products = products
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManageProducts(int id, List<int>? selectedProductIds)
        {
            var collection = await _db.Collections
                .Include(c => c.ProductCollections)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (collection == null) return NotFound();

            var requested = (selectedProductIds ?? new List<int>())
                .Where(x => x > 0).Distinct().ToList();

            // Discard non-existent IDs rather than adding invalid foreign keys.
            var selected = await _db.Products.AsNoTracking()
                .Where(p => requested.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync();
            var selectedSet = selected.ToHashSet();
            var previousSet = collection.ProductCollections
                .Select(pc => pc.ProductId).ToHashSet();

            foreach (var link in collection.ProductCollections
                .Where(pc => !selectedSet.Contains(pc.ProductId)).ToList())
                _db.ProductCollections.Remove(link);

            foreach (var productId in selected.Where(x => !previousSet.Contains(x)))
                _db.ProductCollections.Add(new ProductCollection
                {
                    CollectionId = id,
                    ProductId = productId,
                    DisplayOrder = 0
                });

            try
            {
                await _db.SaveChangesAsync();
                TempData["Success"] = "Collection products updated successfully.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Manage products for collection {Id} failed", id);
                TempData["Error"] = "Could not save product assignments.";
                return RedirectToAction(nameof(ManageProducts), new { id });
            }
        }

        private void NormalizeAndValidate(CollectionFormViewModel model)
        {
            model.Name = model.Name?.Trim() ?? "";
            model.Description = string.IsNullOrWhiteSpace(model.Description)
                ? null : model.Description.Trim();

            var slug = CatalogSlug.FromName(model.Name, "collection", 120);
            model.Slug = slug;
            ModelState.Remove(nameof(model.Slug));

            if (string.IsNullOrWhiteSpace(model.Name))
                ModelState.AddModelError(nameof(model.Name), "Collection name is required.");
            if (model.Name.Length > 100)
                ModelState.AddModelError(nameof(model.Name), "Name cannot exceed 100 characters.");
            if (model.Description?.Length > 500)
                ModelState.AddModelError(nameof(model.Description), "Description cannot exceed 500 characters.");
            if (string.IsNullOrWhiteSpace(slug))
                ModelState.AddModelError(nameof(model.Slug), "Use a name or slug containing English letters or numbers.");
            if (model.DisplayOrder < 0)
                ModelState.AddModelError(nameof(model.DisplayOrder), "Display order cannot be negative.");

            if (model.ImageFile != null)
            {
                var error = ValidateImage(model.ImageFile);
                if (error != null) ModelState.AddModelError(nameof(model.ImageFile), error);
            }
        }

        private async Task CheckSlugAvailableAsync(string? slug, int? excludeId)
        {
            if (string.IsNullOrWhiteSpace(slug)) return;
            var exists = await _db.Collections.AnyAsync(c => c.Slug == slug &&
                (!excludeId.HasValue || c.Id != excludeId.Value));
            if (exists) ModelState.AddModelError(nameof(CollectionFormViewModel.Slug),
                "That collection URL slug is already in use.");
        }

        private static string MakeSlug(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            return slug.Length > 120 ? slug[..120].TrimEnd('-') : slug;
        }

        private static string? ValidateImage(IFormFile file)
        {
            if (file.Length <= 0) return "Choose a valid image.";
            if (file.Length > MaxImageBytes) return "Maximum image size is 5 MB.";
            var ext = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(ext)) return "Use JPG, JPEG, PNG or WEBP.";
            var contentType = file.ContentType.ToLowerInvariant();
            if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
                return "Invalid image content type.";
            return null;
        }

        private async Task<string> SaveImageAsync(IFormFile image)
        {
            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            var filename = $"{Guid.NewGuid():N}{extension}";
            var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(root, "uploads", "collections");
            Directory.CreateDirectory(folder);
            var fullPath = Path.Combine(folder, filename);
            try
            {
                await using var stream = new FileStream(fullPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None);
                await image.CopyToAsync(stream);
            }
            catch
            {
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                throw;
            }
            return $"/uploads/collections/{filename}";
        }

        private void DeleteImageQuietly(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return;
            // Never allow an arbitrary value to delete outside our uploaded-image directory.
            if (!imageUrl.StartsWith("/uploads/collections/", StringComparison.Ordinal)) return;
            var filename = Path.GetFileName(imageUrl);
            var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var fullPath = Path.Combine(root, "uploads", "collections", filename);
            try
            {
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete collection image {Image}", filename);
            }
        }
    }
}
