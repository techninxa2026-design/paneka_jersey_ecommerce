
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<CategoriesController> _logger;

        private const long MaxImageSize = 5 * 1024 * 1024;

        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".webp"
            };

        public CategoriesController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            ILogger<CategoriesController> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? search)
        {
            var query = _context.Categories
                .AsNoTracking()
                .Include(c => c.Products)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(c =>
                    c.Name.Contains(search) ||
                    c.Slug.Contains(search) ||
                    (c.Description != null &&
                     c.Description.Contains(search)));
            }

            ViewData["Search"] = search;

            var categories = await query
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return View(categories);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new CategoryFormViewModel
            {
                IsActive = true,
                DisplayOrder = 0
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> Create(
            CategoryFormViewModel model)
        {
            model.Name = model.Name?.Trim() ?? "";

            var slug = await CatalogSlug.UniqueAsync(model.Name, "category", 120,
                candidate => _context.Categories.AnyAsync(x => x.Slug == candidate));

            model.Slug = slug;

            // The slug is normalized on the server.
            ModelState.Remove(nameof(model.Slug));

            if (string.IsNullOrWhiteSpace(slug))
            {
                ModelState.AddModelError(
                    nameof(model.Slug),
                    "Enter a valid category name or slug.");
            }

            if (model.ImageFile == null)
            {
                ModelState.AddModelError(
                    nameof(model.ImageFile),
                    "Please select a category image.");
            }
            else
            {
                var error = ValidateImage(model.ImageFile);

                if (error != null)
                {
                    ModelState.AddModelError(
                        nameof(model.ImageFile), error);
                }
            }

            if (!string.IsNullOrWhiteSpace(slug) &&
                await _context.Categories.AnyAsync(
                    c => c.Slug == slug))
            {
                ModelState.AddModelError(
                    nameof(model.Slug),
                    "This category slug already exists.");
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning(
                    "Category creation validation failed: {Errors}",
                    string.Join("; ",
                        ModelState
                            .Where(x => x.Value?.Errors.Count > 0)
                            .SelectMany(x => x.Value!.Errors
                                .Select(e =>
                                    $"{x.Key}: {e.ErrorMessage}"))));

                return View(model);
            }

            string? uploadedImage = null;

            try
            {
                uploadedImage =
                    await SaveImageAsync(model.ImageFile!);

                var category = new Category
                {
                    Name = model.Name,
                    Slug = slug,
                    Description = model.Description?.Trim(),
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive,
                    ImageUrl = uploadedImage,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Categories.Add(category);
                await _context.SaveChangesAsync();

                TempData["Success"] =
                    "Category created successfully.";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (
                ex is DbUpdateException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                _logger.LogError(
                    ex,
                    "Failed to create category {CategoryName}",
                    model.Name);

                if (uploadedImage != null)
                {
                    DeleteUploadedImage(uploadedImage);
                }

                ModelState.AddModelError(
                    "",
                    _environment.IsDevelopment()
                        ? ex.GetBaseException().Message
                        : "Could not create category. Check server logs for details.");

                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var category = await _context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
                return NotFound();

            return View(new CategoryFormViewModel
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                Description = category.Description,
                DisplayOrder = category.DisplayOrder,
                IsActive = category.IsActive,
                ImageUrl = category.ImageUrl
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> Edit(
            int id,
            CategoryFormViewModel model)
        {
            if (id != model.Id)
                return NotFound();

            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
                return NotFound();

            model.Name = model.Name?.Trim() ?? "";

            var slug = string.Equals(category.Name, model.Name, StringComparison.OrdinalIgnoreCase)
                ? category.Slug
                : await CatalogSlug.UniqueAsync(model.Name, "category", 120,
                    candidate => _context.Categories.AnyAsync(x => x.Slug == candidate && x.Id != id));

            model.Slug = slug;
            model.ImageUrl = category.ImageUrl;

            ModelState.Remove(nameof(model.Slug));

            if (string.IsNullOrWhiteSpace(slug))
            {
                ModelState.AddModelError(
                    nameof(model.Slug),
                    "Enter a valid slug.");
            }

            if (model.ImageFile != null)
            {
                var error = ValidateImage(model.ImageFile);

                if (error != null)
                {
                    ModelState.AddModelError(
                        nameof(model.ImageFile), error);
                }
            }

            if (!string.IsNullOrWhiteSpace(slug) &&
                await _context.Categories.AnyAsync(
                    c => c.Slug == slug && c.Id != id))
            {
                ModelState.AddModelError(
                    nameof(model.Slug),
                    "This slug already exists.");
            }

            if (!ModelState.IsValid)
                return View(model);

            string? newImage = null;
            var oldImage = category.ImageUrl;

            try
            {
                if (model.ImageFile != null)
                {
                    newImage =
                        await SaveImageAsync(model.ImageFile);
                }

                category.Name = model.Name;
                category.Slug = slug;
                category.Description =
                    model.Description?.Trim();
                category.DisplayOrder = model.DisplayOrder;
                category.IsActive = model.IsActive;

                if (newImage != null)
                {
                    category.ImageUrl = newImage;
                }

                await _context.SaveChangesAsync();

                if (newImage != null)
                {
                    DeleteUploadedImage(oldImage);
                }

                TempData["Success"] =
                    "Category updated successfully.";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (
                ex is DbUpdateException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                _logger.LogError(
                    ex,
                    "Failed to update category {Id}", id);

                if (newImage != null)
                {
                    DeleteUploadedImage(newImage);
                }

                ModelState.AddModelError(
                    "",
                    _environment.IsDevelopment()
                        ? ex.GetBaseException().Message
                        : "Could not update category.");

                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.Categories
                .Include(c => c.Products)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
            {
                TempData["Error"] = "Category not found.";
                return RedirectToAction(nameof(Index));
            }

            if (category.Products.Any())
            {
                TempData["Error"] =
                    "Cannot delete a category containing products.";

                return RedirectToAction(nameof(Index));
            }

            var imageUrl = category.ImageUrl;

            try
            {
                _context.Categories.Remove(category);
                await _context.SaveChangesAsync();

                DeleteUploadedImage(imageUrl);

                TempData["Success"] =
                    "Category deleted successfully.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to delete category {Id}", id);

                TempData["Error"] =
                    "Could not delete category because it is referenced by other records.";
            }

            return RedirectToAction(nameof(Index));
        }

        private static string? ValidateImage(IFormFile file)
        {
            if (file.Length <= 0)
                return "Select a valid image.";

            if (file.Length > MaxImageSize)
                return "Image cannot exceed 5 MB.";

            var extension = Path.GetExtension(file.FileName);

            if (!AllowedExtensions.Contains(extension))
                return "Only JPG, JPEG, PNG and WEBP are allowed.";

            var contentType = file.ContentType.ToLowerInvariant();

            if (contentType != "image/jpeg" &&
                contentType != "image/png" &&
                contentType != "image/webp")
            {
                return "Invalid image content type.";
            }

            return null;
        }

        private async Task<string> SaveImageAsync(
            IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName)
                .ToLowerInvariant();

            var fileName = $"{Guid.NewGuid():N}{extension}";

            var root = _environment.WebRootPath ??
                Path.Combine(
                    _environment.ContentRootPath,
                    "wwwroot");

            var folder = Path.Combine(
                root, "uploads", "categories");

            Directory.CreateDirectory(folder);

            var fullPath = Path.Combine(folder, fileName);

            try
            {
                await using var stream = new FileStream(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);

                await file.CopyToAsync(stream);
            }
            catch
            {
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);

                throw;
            }

            return $"/uploads/categories/{fileName}";
        }

        private void DeleteUploadedImage(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var fileName = Path.GetFileName(imageUrl);

            var root = _environment.WebRootPath ??
                Path.Combine(
                    _environment.ContentRootPath,
                    "wwwroot");

            var path = Path.Combine(
                root, "uploads", "categories", fileName);

            try
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to delete category image {Path}",
                    path);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to delete category image {Path}",
                    path);
            }
        }

        private static string GenerateSlug(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var slug = Regex.Replace(
                value.Trim().ToLowerInvariant(),
                @"[^a-z0-9]+", "-").Trim('-');

            return slug.Length > 120
                ? slug[..120].TrimEnd('-')
                : slug;
        }
    }
}
