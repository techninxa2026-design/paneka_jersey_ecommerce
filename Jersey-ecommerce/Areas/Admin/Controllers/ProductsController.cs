using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Jersey_ecommerce.Areas.Admin.ViewModels.Products;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/[controller]/[action]/{id?}")]
public sealed class ProductsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ProductsController> _logger;
    private const int PageSize = 12;
    private const int MaxGalleryImages = 10;
    private const long MaxImageBytes = 5L * 1024 * 1024;
    private const string UploadRoot = "uploads/products";
    private static readonly string[] PathProperties = { "ImageUrl", "ImagePath", "FilePath", "Url", "Path" };
    private static readonly Regex SlugNonAlpha = new("[^a-z0-9]+", RegexOptions.Compiled);

    public ProductsController(ApplicationDbContext db, IWebHostEnvironment environment, ILogger<ProductsController> logger)
    {
        _db = db;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet("/Admin/Products")]
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string status = "all", int? categoryId = null, int page = 1)
    {
        page = Math.Max(1, page);
        search = search?.Trim();
        status = status is "published" or "draft" ? status : "all";

        var query = _db.Products.AsNoTracking()
            .Include(x => x.Category).Include(x => x.Brand)
            .Include(x => x.Images).Include(x => x.Variants)
            .AsSplitQuery().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Slug.Contains(search)
                || (p.SKU != null && p.SKU.Contains(search))
                || p.Variants.Any(v => v.SKU != null && v.SKU.Contains(search)));
        if (categoryId is > 0) query = query.Where(p => p.CategoryId == categoryId);
        if (status == "published") query = query.Where(p => p.IsPublished);
        if (status == "draft") query = query.Where(p => !p.IsPublished);

        var matches = await query.CountAsync();
        int totalPages = Math.Max(1, (int)Math.Ceiling(matches / (double)PageSize));
        page = Math.Min(page, totalPages);
        var products = await query.OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();

        // IMPORTANT: one stock-unit count per active VARIANT, not per product.
        // This total counts the entire catalog rather than merely the current page.
        long available = await _db.ProductVariants.AsNoTracking().Where(v => v.IsActive)
            .SumAsync(v => (long?)v.StockQuantity) ?? 0;

        var vm = new ProductIndexViewModel
        {
            Search = search, Status = status, CategoryId = categoryId, Page = page,
            TotalPages = totalPages, MatchingCount = matches,
            TotalProducts = await _db.Products.CountAsync(),
            PublishedProducts = await _db.Products.CountAsync(p => p.IsPublished),
            DraftProducts = await _db.Products.CountAsync(p => !p.IsPublished),
            TotalAvailableUnits = available,
            OutOfStockProducts = await _db.Products.CountAsync(p =>
                !p.Variants.Any(v => v.IsActive && v.StockQuantity > 0)),
            Categories = await _db.Categories.AsNoTracking().OrderBy(c => c.Name)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(),
            Products = products.Select(ToListRow).ToList()
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var p = await FullProduct(id, tracked: false);
        if (p is null) return NotFound();
        var sizes = await _db.Sizes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        var colors = await _db.Colors.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        var vm = new ProductDetailsViewModel
        {
            Product = ToListRow(p), ShortDescription = p.ShortDescription,
            Description = p.Description, CompareAtPrice = p.CompareAtPrice,
            IsNewArrival = p.IsNewArrival,
            Photos = Gallery(p).Select(ToPhoto).ToList(),
            Variants = p.Variants.OrderBy(v => v.SizeId).ThenBy(v => v.ColorId)
                .Select(v => new VariantDetailsRowViewModel
                {
                    Size = sizes.GetValueOrDefault(v.SizeId, "Unknown size"),
                    Color = colors.GetValueOrDefault(v.ColorId, "Unknown color"),
                    SKU = v.SKU ?? "", Price = v.Price,
                    StockQuantity = v.StockQuantity, IsActive = v.IsActive,
                    ImageUrl = FindVariantImage(p, v)
                }).ToList()
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new ProductFormViewModel { IsPublished = false };
        await PopulateLookups(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100L * 1024 * 1024)]
    public async Task<IActionResult> Create(ProductFormViewModel vm)
    {
        NormalizeFormCollections(vm);
        ModelState.Remove(nameof(vm.Slug)); // URL slugs are never supplied by the browser.
        await PopulateLookups(vm);
        await ValidateForm(vm);
        if (!ModelState.IsValid)
        {
            ViewData["FormError"] = "Product was not created. Correct the errors below and try again.";
            return View(vm);
        }

        var addedFiles = new List<string>();
        var product = new Product();
        try
        {
            MapFields(vm, product);
            product.Slug = await UniqueSlug(null, vm.Name);
            _db.Products.Add(product);
            await SyncVariants(product, vm.Variants, addedFiles);
            SynchronizeLegacyStock(product);
            await AddGalleryImages(product, vm.NewImages, addedFiles);
            SetPrimary(product, vm.PrimaryImageId);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Product created successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            DeletePhysicalFiles(addedFiles);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Error creating product");
            ModelState.AddModelError(string.Empty,
                "The product could not be saved. Please check the form and try again.");
            ViewData["FormError"] = "Creation failed; no product was saved.";
            return View(vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await FullProduct(id, tracked: false);
        if (product == null) return NotFound();
        var vm = new ProductFormViewModel
        {
            Id = product.Id, Name = product.Name, Slug = product.Slug,
            ShortDescription = product.ShortDescription, Description = product.Description,
            CategoryId = product.CategoryId, BrandId = product.BrandId,
            SKU = product.SKU, BasePrice = product.BasePrice,
            CompareAtPrice = product.CompareAtPrice, IsFeatured = product.IsFeatured,
            IsNewArrival = product.IsNewArrival, IsPublished = product.IsPublished,
            ExistingImages = Gallery(product).Select(ToPhoto).ToList(),
            PrimaryImageId = Gallery(product).FirstOrDefault(IsPrimary)?.Id,
            Variants = product.Variants.OrderBy(v => v.SizeId).ThenBy(v => v.ColorId)
                .Select(v => new VariantInputViewModel
                {
                    SizeId = v.SizeId, ColorId = v.ColorId, SKU = v.SKU,
                    StockQuantity = v.StockQuantity, Price = v.Price,
                    IsActive = v.IsActive, ExistingImageUrl = FindVariantImage(product, v)
                }).ToList()
        };
        await PopulateLookups(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100L * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel vm)
    {
        if (id != vm.Id) return BadRequest("Product ID mismatch.");
        NormalizeFormCollections(vm);
        ModelState.Remove(nameof(vm.Slug));
        var product = await FullProduct(id, tracked: true);
        if (product == null) return NotFound();

        // The browser does not post URLs for existing images. Restore the
        // previews from the database while keeping the submitted input values.
        HydrateExistingImages(vm, product);
        await PopulateLookups(vm);
        await ValidateForm(vm);
        if (!ModelState.IsValid)
        {
            ViewData["FormError"] = "Changes were not saved. Correct the errors below and try again.";
            return View(vm);
        }

        var addedFiles = new List<string>();
        var pendingDeletions = new List<string>();
        try
        {
            var nameChanged = !string.Equals(product.Name, vm.Name.Trim(), StringComparison.OrdinalIgnoreCase);
            MapFields(vm, product);
            if (nameChanged)
                product.Slug = await UniqueSlug(null, vm.Name, product.Id);
            product.UpdatedAt = DateTime.UtcNow;

            foreach (var img in Gallery(product)
                .Where(x => vm.RemoveImageIds.Contains(x.Id)).ToList())
            {
                var path = ImagePath(img);
                if (!string.IsNullOrWhiteSpace(path)) pendingDeletions.Add(path);
                // Do not also detach from product.Images. EF Core will see a
                // required FK as conceptually null if we sever the association.
                _db.Entry(img).State = EntityState.Deleted;
            }

            await SyncVariants(product, vm.Variants, addedFiles, pendingDeletions);
            SynchronizeLegacyStock(product);
            await AddGalleryImages(product, vm.NewImages, addedFiles);
            SetPrimary(product, vm.PrimaryImageId);
            await _db.SaveChangesAsync();
            DeletePhysicalFiles(pendingDeletions);
            TempData["Success"] = "Product updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            DeletePhysicalFiles(addedFiles);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Error editing product {ProductId}", id);
            ModelState.AddModelError(string.Empty,
                "Changes could not be saved. Please check the form and try again.");
            ViewData["FormError"] = "Update failed; no changes were committed.";
            var persisted = await FullProduct(id, tracked: false);
            if (persisted != null) HydrateExistingImages(vm, persisted);
            return View(vm);
        }
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
        {
            TempData["Error"] = "Product not found.";
            return RedirectToAction(nameof(Index));
        }

        // Collect uploaded image paths before database deletion.
        var imagePaths = product.Images
            .Select(ImagePath)
            .Concat(product.Variants.Select(VariantPath))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        try
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync();

            // 1. Delete product-collection relationships.
            await _db.Set<ProductCollection>()
                .Where(x => EF.Property<int>(x, "ProductId") == id)
                .ExecuteDeleteAsync();

            // 2. Delete gallery and variant images.
            await _db.Set<ProductImage>()
                .Where(x => EF.Property<int>(x, "ProductId") == id)
                .ExecuteDeleteAsync();

            // 3. Delete all size/color variants.
            await _db.Set<ProductVariant>()
                .Where(x => x.ProductId == id)
                .ExecuteDeleteAsync();

            // 4. Delete the product itself.
            var deleted = await _db.Products
                .Where(x => x.Id == id)
                .ExecuteDeleteAsync();

            if (deleted != 1)
            {
                throw new InvalidOperationException(
                    "Product could not be deleted."
                );
            }

            await transaction.CommitAsync();

            // Delete uploaded files only after database commit.
            DeletePhysicalFiles(imagePaths);

            TempData["Success"] =
                "Product and associated inventory deleted successfully.";
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Product {ProductId} has dependent records.",
                id
            );

            TempData["Error"] =
                "This product is referenced by orders or other records.";
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error deleting product {ProductId}",
                id
            );

            TempData["Error"] =
                "Could not delete product. Check server logs.";
        }

        return RedirectToAction(nameof(Index));
    }


    private IQueryable<Product> QueryAll() => _db.Products
        .Include(p => p.Category).Include(p => p.Brand)
        .Include(p => p.Images).Include(p => p.Variants).AsSplitQuery();

    private Task<Product?> FullProduct(int id, bool tracked)
    {
        var query = QueryAll();
        if (!tracked) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(p => p.Id == id);
    }

    private static ProductListRowViewModel ToListRow(Product p)
    {
        var image = Gallery(p).FirstOrDefault(IsPrimary) ?? Gallery(p).FirstOrDefault();
        return new ProductListRowViewModel
        {
            Id = p.Id, Name = p.Name, Slug = p.Slug, SKU = p.SKU,
            CategoryName = p.Category?.Name ?? "Uncategorized", BrandName = p.Brand?.Name,
            ImageUrl = image is null ? null : ImagePath(image), Price = p.BasePrice,
            AvailableQuantity = p.AvailableQuantity,
            VariantCount = p.Variants.Count, IsPublished = p.IsPublished,
            IsFeatured = p.IsFeatured, CreatedAt = p.CreatedAt
        };
    }

    private static void MapFields(ProductFormViewModel vm, Product p)
    {
        p.Name = vm.Name.Trim();
        p.ShortDescription = vm.ShortDescription?.Trim();
        p.Description = vm.Description?.Trim();
        p.CategoryId = vm.CategoryId; p.BrandId = vm.BrandId;
        p.SKU = string.IsNullOrWhiteSpace(vm.SKU) ? null : vm.SKU.Trim();
        p.BasePrice = vm.BasePrice; p.CompareAtPrice = vm.CompareAtPrice;
        p.IsFeatured = vm.IsFeatured; p.IsNewArrival = vm.IsNewArrival;
        p.IsPublished = vm.IsPublished;
    }

    private async Task PopulateLookups(ProductFormViewModel vm)
    {
        vm.Categories = await _db.Categories.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync();
        vm.Brands = await _db.Brands.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync();
        vm.Sizes = await _db.Sizes.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new LookupViewModel { Id = x.Id, Name = x.Name }).ToListAsync();
        vm.Colors = await _db.Colors.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new LookupViewModel { Id = x.Id, Name = x.Name }).ToListAsync();
        // Color swatches are optional: names still work without a HexCode field.
    }

    private async Task ValidateForm(ProductFormViewModel vm)
    {
        if (!await _db.Categories.AnyAsync(c => c.Id == vm.CategoryId))
            ModelState.AddModelError(nameof(vm.CategoryId), "Select an existing category.");
        if (vm.BrandId.HasValue && !await _db.Brands.AnyAsync(b => b.Id == vm.BrandId.Value))
            ModelState.AddModelError(nameof(vm.BrandId), "Select an existing brand.");
        if (vm.CompareAtPrice.HasValue && vm.CompareAtPrice < vm.BasePrice)
            ModelState.AddModelError(nameof(vm.CompareAtPrice), "Compare-at price must be at least the base price.");

        var allSizes = vm.Sizes.Select(x => x.Id).ToHashSet();
        var allColors = vm.Colors.Select(x => x.Id).ToHashSet();
        var keys = new HashSet<(int, int)>();
        var skuSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < vm.Variants.Count; i++)
        {
            var variant = vm.Variants[i];
            if (!allSizes.Contains(variant.SizeId) || !allColors.Contains(variant.ColorId))
                ModelState.AddModelError($"Variants[{i}]", "Choose valid size and color values.");
            if (!keys.Add((variant.SizeId, variant.ColorId)))
                ModelState.AddModelError($"Variants[{i}]", "Duplicate size/color combination.");
            if (variant.StockQuantity < 0)
                ModelState.AddModelError($"Variants[{i}].StockQuantity", "Stock cannot be negative.");
            if (variant.Price is < 0)
                ModelState.AddModelError($"Variants[{i}].Price", "Variant price cannot be negative.");
            if (!string.IsNullOrWhiteSpace(variant.SKU))
            {
                var sku = variant.SKU.Trim();
                if (sku.Length > 100)
                    ModelState.AddModelError($"Variants[{i}].SKU", "SKU must be at most 100 characters.");
                if (!skuSet.Add(sku))
                    ModelState.AddModelError($"Variants[{i}].SKU", "Variant SKUs must be unique.");
                if (await _db.ProductVariants.AnyAsync(v => v.SKU == sku && v.ProductId != vm.Id))
                    ModelState.AddModelError($"Variants[{i}].SKU", "This SKU already belongs to another product.");
            }
            CheckUpload(variant.Image, $"Variants[{i}].Image");
            if (variant.Image != null && !SupportsVariantImageStorage())
                ModelState.AddModelError($"Variants[{i}].Image", "Variant images need ProductVariant.ImageUrl or ProductImage.AltText/ColorId (see README).");
        }
        if (vm.IsPublished && vm.Variants.Count == 0)
            ModelState.AddModelError(nameof(vm.Variants), "Add at least one size/color variant before publishing.");
        if (vm.Variants.Count > 200)
            ModelState.AddModelError(nameof(vm.Variants), "A product may have at most 200 size/color combinations.");
        foreach (var (file, i) in vm.NewImages.Select((f, i) => (f, i)))
            CheckUpload(file, $"NewImages[{i}]");
        if (vm.NewImages.Count > MaxGalleryImages)
            ModelState.AddModelError(nameof(vm.NewImages), "Maximum 10 gallery images per product.");
        if (vm.Id == 0 && vm.NewImages.Count == 0)
            ModelState.AddModelError(nameof(vm.NewImages), "Upload at least one gallery image.");
        if (vm.Id != 0)
        {
            var product = await FullProduct(vm.Id, tracked: false);
            if (product == null) { ModelState.AddModelError("", "Product not found."); return; }
            var kept = Gallery(product).Count(img => !vm.RemoveImageIds.Contains(img.Id));
            if (vm.RemoveImageIds.Any(imgId => !Gallery(product).Any(img => img.Id == imgId)))
                ModelState.AddModelError(nameof(vm.RemoveImageIds), "Unknown gallery image selected for removal.");
            // Removing the current cover is allowed; SetPrimary will choose a
            // surviving image. An unrelated photo ID is still rejected.
            if (vm.PrimaryImageId.HasValue &&
                !Gallery(product).Any(img => img.Id == vm.PrimaryImageId.Value))
                ModelState.AddModelError(nameof(vm.PrimaryImageId), "The chosen cover image does not belong to this product.");
            if (kept + vm.NewImages.Count > MaxGalleryImages)
                ModelState.AddModelError(nameof(vm.NewImages), "A product can have at most 10 gallery images.");
            if (kept + vm.NewImages.Count == 0)
                ModelState.AddModelError(nameof(vm.NewImages), "Keep or upload at least one gallery image.");
        }
    }

    private static void NormalizeFormCollections(ProductFormViewModel vm)
    {
        vm.Variants ??= new();
        vm.NewImages ??= new();
        vm.RemoveImageIds ??= new();
    }

    private void HydrateExistingImages(ProductFormViewModel vm, Product product)
    {
        vm.ExistingImages = Gallery(product).Select(ToPhoto).ToList();
        var existing = product.Variants.ToDictionary(v => (v.SizeId, v.ColorId));
        foreach (var row in vm.Variants)
        {
            if (existing.TryGetValue((row.SizeId, row.ColorId), out var variant))
                row.ExistingImageUrl = FindVariantImage(product, variant);
        }
    }

    private IEnumerable<ProductImage> ActiveGallery(Product product) =>
        Gallery(product).Where(image => _db.Entry(image).State != EntityState.Deleted);

    private void CheckUpload(IFormFile? file, string field)
    {
        if (file == null) return;
        if (file.Length <= 0 || file.Length > MaxImageBytes)
            ModelState.AddModelError(field, "Each image must be 1 byte to 5 MB.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            ModelState.AddModelError(field, "Upload JPG, PNG or WEBP images only.");
        if (file.ContentType.ToLowerInvariant() is not ("image/jpeg" or "image/png" or "image/webp"))
            ModelState.AddModelError(field, "Invalid image MIME type.");
    }

    private async Task SyncVariants(Product p, List<VariantInputViewModel> inputs,
        List<string> addedFiles, List<string>? deletions = null)
    {
        deletions ??= new List<string>();
        var keep = inputs.Select(i => (i.SizeId, i.ColorId)).ToHashSet();
        foreach (var old in p.Variants.Where(v => !keep.Contains((v.SizeId, v.ColorId))).ToList())
        {
            var oldPath = VariantPath(old);
            if (!string.IsNullOrEmpty(oldPath)) deletions.Add(oldPath);
            RemoveFallbackVariantImage(p, old.SizeId, old.ColorId, deletions);
            _db.Entry(old).State = EntityState.Deleted;
            // Leave the relationship attached until EF persists the deletion.
        }

        foreach (var input in inputs)
        {
            var variant = p.Variants.FirstOrDefault(v =>
                _db.Entry(v).State != EntityState.Deleted &&
                v.SizeId == input.SizeId && v.ColorId == input.ColorId);
            if (variant == null)
            {
                variant = new ProductVariant { SizeId = input.SizeId, ColorId = input.ColorId };
                p.Variants.Add(variant);
            }
            variant.StockQuantity = input.StockQuantity;
            variant.IsActive = input.IsActive;
            variant.Price = input.Price ?? p.BasePrice;
            variant.SKU = !string.IsNullOrWhiteSpace(input.SKU)
                ? input.SKU.Trim()
                : AutoSku(p.SKU, p.Slug, input.SizeId, input.ColorId);

            if (input.RemoveImage || input.Image != null)
            {
                var oldPath = VariantPath(variant);
                if (!string.IsNullOrWhiteSpace(oldPath)) deletions.Add(oldPath);
                SetString(variant, PathProperties, null);
                RemoveFallbackVariantImage(p, input.SizeId, input.ColorId, deletions);
            }
            if (input.Image != null)
            {
                var uploaded = await SaveImage(input.Image, addedFiles);
                if (!SetString(variant, PathProperties, uploaded))
                {
                    var photo = new ProductImage();
                    if (!SetString(photo, PathProperties, uploaded))
                        throw new InvalidOperationException("ProductImage must have an ImageUrl or equivalent field.");
                    FillUploadMetadata(photo, input.Image, VariantMarker(input.SizeId, input.ColorId));
                    SetInt(photo, new[] { "ColorId" }, input.ColorId);
                    SetBool(photo, new[] { "IsPrimary", "IsMain" }, false);
                    p.Images.Add(photo);
                }
            }
        }
    }


    private void SynchronizeLegacyStock(Product product)
    {
        // Exclude children marked for deletion; the collection is intentionally
        // not detached in order to avoid required-relationship errors.
        var total = product.Variants
            .Where(v => _db.Entry(v).State != EntityState.Deleted && v.IsActive)
            .Sum(v => (long)v.StockQuantity);
        product.StockQuantity = (int)Math.Min(int.MaxValue, total);
    }

    private static void FillUploadMetadata(ProductImage image, IFormFile file, string alt)
    {
        SetString(image, new[] { "AltText" }, alt);
        SetString(image, new[] { "OriginalFileName", "FileName" }, Path.GetFileName(file.FileName));
        SetString(image, new[] { "ContentType", "MimeType" }, file.ContentType);
        foreach (var field in new[] { "FileSize", "SizeInBytes" })
        {
            var property = image.GetType().GetProperty(field);
            if (property == null || !property.CanWrite) continue;
            if (property.PropertyType == typeof(long) || property.PropertyType == typeof(long?)) property.SetValue(image, file.Length);
            else if (property.PropertyType == typeof(int) || property.PropertyType == typeof(int?)) property.SetValue(image, (int)Math.Min(int.MaxValue, file.Length));
            break;
        }
    }

    private static string AutoSku(string? productSku, string? slug, int size, int color)
    {
        var seed = string.IsNullOrWhiteSpace(productSku) ? slug ?? "PRODUCT" : productSku;
        seed = Regex.Replace(seed!.ToUpperInvariant(), "[^A-Z0-9]+", "-").Trim('-');
        seed = seed.Length > 70 ? seed[..70] : seed;
        return $"{seed}-S{size}-C{color}";
    }

    private async Task AddGalleryImages(Product product, IEnumerable<IFormFile> files, List<string> added)
    {
        foreach (var file in files.Where(f => f.Length > 0))
        {
            var path = await SaveImage(file, added);
            var image = new ProductImage();
            if (!SetString(image, PathProperties, path))
                throw new InvalidOperationException("ProductImage needs an ImageUrl/ImagePath/FilePath property.");
            FillUploadMetadata(image, file, product.Name);
            SetInt(image, new[] { "DisplayOrder", "SortOrder" }, ActiveGallery(product).Count());
            SetBool(image, new[] { "IsPrimary", "IsMain" }, ActiveGallery(product).Count() == 0);
            product.Images.Add(image);
        }
    }

    private void SetPrimary(Product product, int? imageId)
    {
        var gallery = ActiveGallery(product).ToList();
        if (gallery.Count == 0) return;
        var chosen = gallery.FirstOrDefault(i => i.Id == imageId) ?? gallery.FirstOrDefault(IsPrimary) ?? gallery[0];
        foreach (var image in gallery)
            SetBool(image, new[] { "IsPrimary", "IsMain" }, ReferenceEquals(image, chosen));
    }

    private static IEnumerable<ProductImage> Gallery(Product p) => p.Images.Where(i => !IsVariantPhoto(i))
        .OrderByDescending(IsPrimary).ThenBy(i => GetInt(i, "DisplayOrder", "SortOrder")).ThenBy(i => i.Id);

    private static bool IsVariantPhoto(ProductImage photo)
    {
        var alt = GetString(photo, "AltText") ?? "";
        return alt.StartsWith("variant:", StringComparison.Ordinal) ||
               (GetInt(photo, "ColorId") > 0 && string.IsNullOrWhiteSpace(alt));
    }

    private static string VariantMarker(int size, int color) => $"variant:{size}:{color}";

    private static string? FindVariantImage(Product p, ProductVariant variant)
    {
        var direct = VariantPath(variant);
        if (!string.IsNullOrWhiteSpace(direct)) return direct;
        var marker = VariantMarker(variant.SizeId, variant.ColorId);
        var image = p.Images.FirstOrDefault(x =>
            string.Equals(GetString(x, "AltText"), marker, StringComparison.Ordinal));
        return image == null ? null : ImagePath(image);
    }

    private void RemoveFallbackVariantImage(Product product, int size, int color, List<string> deletions)
    {
        var marker = VariantMarker(size, color);
        // Only remove the exact size/color photo. A color can belong to
        // multiple sizes; deleting one must not erase the others' images.
        foreach (var image in product.Images
            .Where(x => GetString(x, "AltText") == marker &&
                _db.Entry(x).State != EntityState.Deleted).ToList())
        {
            var path = ImagePath(image);
            if (!string.IsNullOrWhiteSpace(path)) deletions.Add(path);
            _db.Entry(image).State = EntityState.Deleted;
        }
    }

    private static bool SupportsVariantImageStorage() =>
        HasStringProperty(typeof(ProductVariant), PathProperties) ||
        (HasStringProperty(typeof(ProductImage), PathProperties) &&
         (HasStringProperty(typeof(ProductImage), new[] { "AltText" }) ||
          typeof(ProductImage).GetProperty("ColorId") != null));

    private static ProductPhotoViewModel ToPhoto(ProductImage image) => new()
    {
        Id = image.Id, Url = ImagePath(image) ?? "", IsPrimary = IsPrimary(image)
    };

    private static bool IsPrimary(ProductImage img) => GetBool(img, "IsPrimary", "IsMain");
    private static string? ImagePath(ProductImage img) => GetString(img, PathProperties);
    private static string? VariantPath(ProductVariant variant) => GetString(variant, PathProperties);

    private static bool HasStringProperty(Type type, IEnumerable<string> propertyNames) =>
        propertyNames.Any(name => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string));

    private static string? GetString(object instance, params string[] names) => GetString(instance, (IEnumerable<string>)names);
    private static string? GetString(object instance, IEnumerable<string> names)
    {
        foreach (var name in names)
            if (instance.GetType().GetProperty(name)?.GetValue(instance) is string value) return value;
        return null;
    }

    private static bool SetString(object instance, IEnumerable<string> names, string? value)
    {
        foreach (var name in names)
        {
            var prop = instance.GetType().GetProperty(name);
            if (prop?.PropertyType == typeof(string) && prop.CanWrite) { prop.SetValue(instance, value); return true; }
        }
        return false;
    }

    private static void SetInt(object instance, IEnumerable<string> names, int value)
    {
        foreach (var name in names)
        {
            var prop = instance.GetType().GetProperty(name);
            if (prop != null && prop.CanWrite && (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?)))
            { prop.SetValue(instance, value); return; }
        }
    }

    private static int GetInt(object instance, params string[] names)
    {
        foreach (var name in names)
            if (instance.GetType().GetProperty(name)?.GetValue(instance) is int value) return value;
        return 0;
    }

    private static bool GetBool(object instance, params string[] names)
    {
        foreach (var name in names)
            if (instance.GetType().GetProperty(name)?.GetValue(instance) is bool value) return value;
        return false;
    }

    private static void SetBool(object instance, IEnumerable<string> names, bool value)
    {
        foreach (var name in names)
        {
            var prop = instance.GetType().GetProperty(name);
            if (prop?.PropertyType == typeof(bool) && prop.CanWrite) { prop.SetValue(instance, value); return; }
        }
    }

    private async Task<string> SaveImage(IFormFile file, List<string> added)
    {
        // Validate actual magic bytes, not just the browser-supplied extension/type.
        await using var input = file.OpenReadStream();
        var header = new byte[12];
        int bytes = await input.ReadAsync(header.AsMemory());
        bool jpeg = bytes >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        bool png = bytes >= 8 && header.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        bool webp = bytes >= 12 && Encoding.ASCII.GetString(header, 0, 4) == "RIFF"
            && Encoding.ASCII.GetString(header, 8, 4) == "WEBP";
        if (!jpeg && !png && !webp) throw new InvalidDataException("Unrecognized image contents.");
        var extension = jpeg ? ".jpg" : png ? ".png" : ".webp";
        var filename = $"{Guid.NewGuid():N}{extension}";
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(root, "uploads", "products");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, filename);
        var publicUrl = $"/{UploadRoot}/{filename}";
        // Register the destination before writing so a mid-stream failure can
        // still be cleaned up by the calling action's catch block.
        added.Add(publicUrl);
        await using (var output = new FileStream(filePath, FileMode.CreateNew))
            await file.CopyToAsync(output);
        return publicUrl;
    }

    private void DeletePhysicalFiles(IEnumerable<string> urls)
    {
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith($"/{UploadRoot}/", StringComparison.Ordinal)) continue;
            var filename = Path.GetFileName(url);
            if (!Regex.IsMatch(filename, "^[a-f0-9]{32}\\.(jpg|png|webp)$", RegexOptions.IgnoreCase)) continue;
            try { System.IO.File.Delete(Path.Combine(root, "uploads", "products", filename)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Unable to delete product image {Image}", filename); }
        }
    }

    private async Task<string> UniqueSlug(string? preferred, string name, int excludeId = 0)
    {
        var source = string.IsNullOrWhiteSpace(preferred) ? name : preferred;
        var decomposed = source.Normalize(NormalizationForm.FormD);
        var withoutMarks = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var seed = SlugNonAlpha.Replace(withoutMarks.ToLowerInvariant(), "-").Trim('-');
        seed = seed.Length > 205 ? seed[..205].TrimEnd('-') : seed;
        if (seed.Length == 0) seed = "product";
        var candidate = seed;
        for (int n = 2; await _db.Products.AnyAsync(p => p.Slug == candidate && p.Id != excludeId); n++)
        {
            var suffix = $"-{n}";
            candidate = seed[..Math.Min(seed.Length, 220 - suffix.Length)] + suffix;
        }
        return candidate;
    }
}
