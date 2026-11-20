using System.Reflection;
using System.Text.RegularExpressions;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CatalogSize = Jersey_ecommerce.Models.Catalog.Size;
using CatalogColor = Jersey_ecommerce.Models.Catalog.Color;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class VariantsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<VariantsController> _logger;

        public VariantsController(ApplicationDbContext db,
            IWebHostEnvironment env, ILogger<VariantsController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // Your Admin sidebar already routes "Sizes & Colors" to /admin/variants.
        [HttpGet]
        public async Task<IActionResult> Index(string? search, string? tab)
        {
            tab = tab is "sizes" or "colors" ? tab : "all";
            search = search?.Trim();

            var sizes = await _db.Sizes.AsNoTracking().ToListAsync();
            var colors = await _db.Colors.AsNoTracking().ToListAsync();
            var sizesUsed = await _db.ProductVariants.AsNoTracking()
                .GroupBy(v => v.SizeId)
                .Select(g => new { Key = g.Key, Count = g.Count() }).ToListAsync();
            var colorsUsed = await _db.ProductVariants.AsNoTracking()
                .GroupBy(v => v.ColorId)
                .Select(g => new { Key = g.Key, Count = g.Count() }).ToListAsync();

            var sizeCounts = sizesUsed.ToDictionary(x => Convert.ToInt32(x.Key), x => x.Count);
            var colorCounts = colorsUsed.ToDictionary(x => Convert.ToInt32(x.Key), x => x.Count);

            var allSizeItems = sizes.Select(s => ToItem(s, sizeCounts)).ToList();
            var allColorItems = colors.Select(c => ToItem(c, colorCounts)).ToList();

            return View(new VariantOptionsIndexViewModel
            {
                Search = search,
                Tab = tab,
                TotalSizes = allSizeItems.Count,
                TotalColors = allColorItems.Count,
                UsedSizes = allSizeItems.Count(x => x.VariantCount > 0),
                UsedColors = allColorItems.Count(x => x.VariantCount > 0),
                Sizes = allSizeItems
                    .Where(x => string.IsNullOrEmpty(search) ||
                        x.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.DisplayOrder ?? int.MaxValue)
                    .ThenBy(x => x.Name)
                    .ToList(),
                Colors = allColorItems
                    .Where(x => string.IsNullOrEmpty(search) ||
                        x.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.DisplayOrder ?? int.MaxValue)
                    .ThenBy(x => x.Name)
                    .ToList()
            });
        }

        [HttpGet]
        public IActionResult CreateSize() => View(NewForm<CatalogSize>(false));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSize(VariantOptionFormViewModel model)
        {
            PrepareForm<CatalogSize>(model, false);
            if (!await ValidateFormAsync(_db.Sizes, model, null)) return View(model);
            try
            {
                var entity = NewEntity<CatalogSize>();
                ApplyForm(entity, model);
                _db.Sizes.Add(entity);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Size '{model.Name}' created successfully.";
                return RedirectToAction(nameof(Index), new { tab = "sizes" });
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                HandleSaveError(ex, "create size");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditSize(int id)
        {
            var item = await _db.Sizes.FindAsync(id);
            if (item == null) return NotFound();
            return View(FormFrom(item, false));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSize(int id, VariantOptionFormViewModel model)
        {
            if (id != model.Id) return BadRequest("Size ID mismatch.");
            var item = await _db.Sizes.FindAsync(id);
            if (item == null) return NotFound();
            PrepareForm<CatalogSize>(model, false);
            if (!await ValidateFormAsync(_db.Sizes, model, id)) return View(model);
            try
            {
                ApplyForm(item, model);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Size updated successfully.";
                return RedirectToAction(nameof(Index), new { tab = "sizes" });
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                HandleSaveError(ex, "update size");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> DeleteSize(int id)
        {
            var item = await _db.Sizes.FindAsync(id);
            if (item == null) return NotFound();
            return View(new VariantOptionDeleteViewModel
            {
                Id = id,
                Name = ReadString(item, "Name", "SizeName", "Label", "Value", "Title") ?? "Size",
                IsColor = false,
                VariantCount = await _db.ProductVariants.CountAsync(v => v.SizeId == id)
            });
        }

        [HttpPost, ActionName("DeleteSize")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSizeConfirmed(int id)
        {
            var item = await _db.Sizes.FindAsync(id);
            if (item == null) return NotFound();
            if (await _db.ProductVariants.AnyAsync(v => v.SizeId == id))
            {
                TempData["Error"] = "Cannot delete a size used by product variants.";
                return RedirectToAction(nameof(Index), new { tab = "sizes" });
            }
            try
            {
                _db.Sizes.Remove(item);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Size deleted successfully.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Cannot delete size {Id}", id);
                TempData["Error"] = "This size is still referenced by other records.";
            }
            return RedirectToAction(nameof(Index), new { tab = "sizes" });
        }

        [HttpGet]
        public IActionResult CreateColor() => View(NewForm<CatalogColor>(true));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateColor(VariantOptionFormViewModel model)
        {
            PrepareForm<CatalogColor>(model, true);
            if (!await ValidateFormAsync(_db.Colors, model, null)) return View(model);
            try
            {
                var entity = NewEntity<CatalogColor>();
                ApplyForm(entity, model);
                _db.Colors.Add(entity);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Color '{model.Name}' created successfully.";
                return RedirectToAction(nameof(Index), new { tab = "colors" });
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                HandleSaveError(ex, "create color");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditColor(int id)
        {
            var item = await _db.Colors.FindAsync(id);
            if (item == null) return NotFound();
            return View(FormFrom(item, true));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditColor(int id, VariantOptionFormViewModel model)
        {
            if (id != model.Id) return BadRequest("Color ID mismatch.");
            var item = await _db.Colors.FindAsync(id);
            if (item == null) return NotFound();
            PrepareForm<CatalogColor>(model, true);
            if (!await ValidateFormAsync(_db.Colors, model, id)) return View(model);
            try
            {
                ApplyForm(item, model);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Color updated successfully.";
                return RedirectToAction(nameof(Index), new { tab = "colors" });
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                HandleSaveError(ex, "update color");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> DeleteColor(int id)
        {
            var item = await _db.Colors.FindAsync(id);
            if (item == null) return NotFound();
            return View(new VariantOptionDeleteViewModel
            {
                Id = id,
                Name = ReadString(item, "Name", "ColorName", "Label", "Value", "Title") ?? "Color",
                IsColor = true,
                VariantCount = await _db.ProductVariants.CountAsync(v => v.ColorId == id)
            });
        }

        [HttpPost, ActionName("DeleteColor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteColorConfirmed(int id)
        {
            var item = await _db.Colors.FindAsync(id);
            if (item == null) return NotFound();
            if (await _db.ProductVariants.AnyAsync(v => v.ColorId == id))
            {
                TempData["Error"] = "Cannot delete a color used by product variants.";
                return RedirectToAction(nameof(Index), new { tab = "colors" });
            }
            try
            {
                _db.Colors.Remove(item);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Color deleted successfully.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Cannot delete color {Id}", id);
                TempData["Error"] = "This color is still referenced by other records.";
            }
            return RedirectToAction(nameof(Index), new { tab = "colors" });
        }

        // The uploaded DbContext confirms Size/Color and their variant FKs, but the
        // exact source for Size.cs/Color.cs wasn't included. These helpers read
        // common property names without requiring you to overwrite your models.
        private static PropertyInfo? FindProperty(Type type, params string[] names)
            => names.Select(n => type.GetProperty(n,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase))
                .FirstOrDefault(p => p != null && p.CanRead && p.CanWrite);

        private static PropertyInfo? LabelProperty(Type type)
            => FindProperty(type, "Name", "SizeName", "ColorName", "Label", "Value", "Title");

        private static PropertyInfo? HexProperty(Type type)
            => FindProperty(type, "HexCode", "HexValue", "HexColor", "ColorHex", "Hex", "ColorCode");

        private static PropertyInfo? OrderProperty(Type type)
            => FindProperty(type, "DisplayOrder", "SortOrder", "SortIndex");

        private static PropertyInfo? ActiveProperty(Type type)
            => FindProperty(type, "IsActive", "Active");

        private static object? Read(object item, PropertyInfo? prop) => prop?.GetValue(item);
        private static string? ReadString(object item, params string[] names)
            => Read(item, FindProperty(item.GetType(), names))?.ToString();
        private static int? ReadInt(object item, PropertyInfo? prop)
            => Read(item, prop) is object n ? Convert.ToInt32(n) : null;
        private static bool? ReadBool(object item, PropertyInfo? prop)
            => Read(item, prop) is object b ? Convert.ToBoolean(b) : null;

        private int ReadId<T>(T item) where T : class
        {
            var key = _db.Model.FindEntityType(typeof(T))?.FindPrimaryKey()?.Properties.SingleOrDefault();
            var info = key?.PropertyInfo ?? FindProperty(typeof(T), "Id");
            return Convert.ToInt32(info?.GetValue(item) ?? 0);
        }

        private VariantOptionItemViewModel ToItem<T>(T item, IDictionary<int, int> counts)
            where T : class
        {
            var id = ReadId(item);
            return new VariantOptionItemViewModel
            {
                Id = id,
                Name = Read(item, LabelProperty(typeof(T)))?.ToString() ?? "(Unnamed)",
                HexCode = Read(item, HexProperty(typeof(T)))?.ToString(),
                DisplayOrder = ReadInt(item, OrderProperty(typeof(T))),
                IsActive = ReadBool(item, ActiveProperty(typeof(T))),
                VariantCount = counts.TryGetValue(id, out var usage) ? usage : 0
            };
        }

        private static VariantOptionFormViewModel NewForm<T>(bool isColor)
        {
            var model = new VariantOptionFormViewModel { IsActive = true };
            PrepareForm<T>(model, isColor);
            if (model.HasHexCode) model.HexCode = "#111111";
            return model;
        }

        private static void PrepareForm<T>(VariantOptionFormViewModel model, bool isColor)
        {
            model.IsColor = isColor;
            model.HasHexCode = isColor && HexProperty(typeof(T)) != null;
            model.HasDisplayOrder = OrderProperty(typeof(T)) != null;
            model.HasIsActive = ActiveProperty(typeof(T)) != null;
        }

        private VariantOptionFormViewModel FormFrom<T>(T item, bool isColor) where T : class
        {
            var model = NewForm<T>(isColor);
            model.Id = ReadId(item);
            model.Name = Read(item, LabelProperty(typeof(T)))?.ToString() ?? "";
            model.HexCode = Read(item, HexProperty(typeof(T)))?.ToString();
            model.DisplayOrder = ReadInt(item, OrderProperty(typeof(T))) ?? 0;
            model.IsActive = ReadBool(item, ActiveProperty(typeof(T))) ?? true;
            return model;
        }

        private async Task<bool> ValidateFormAsync<T>(DbSet<T> set,
            VariantOptionFormViewModel model, int? exceptId) where T : class
        {
            model.Name = model.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(model.Name))
                ModelState.AddModelError(nameof(model.Name), "Name is required.");

            var label = LabelProperty(typeof(T));
            if (label == null || label.PropertyType != typeof(string))
                ModelState.AddModelError("", $"Could not find a writable Name property on {typeof(T).Name}. Share the model source to map it.");
            else
            {
                var max = _db.Model.FindEntityType(typeof(T))?.FindProperty(label.Name)?.GetMaxLength();
                if (max.HasValue && model.Name.Length > max)
                    ModelState.AddModelError(nameof(model.Name), $"Name cannot exceed {max} characters.");
            }

            if (model.HasHexCode)
            {
                model.HexCode = model.HexCode?.Trim() ?? "";
                if (!Regex.IsMatch(model.HexCode, "^#[a-fA-F0-9]{6}$"))
                    ModelState.AddModelError(nameof(model.HexCode), "Enter a hex color like #FF5733.");
            }

            if (model.HasDisplayOrder && model.DisplayOrder < 0)
                ModelState.AddModelError(nameof(model.DisplayOrder), "Display order cannot be negative.");

            if (!string.IsNullOrWhiteSpace(model.Name))
            {
                var all = await set.AsNoTracking().ToListAsync();
                var duplicate = all.Any(item => ReadId(item) != exceptId &&
                    string.Equals(Read(item, label)?.ToString()?.Trim(), model.Name,
                        StringComparison.OrdinalIgnoreCase));
                if (duplicate) ModelState.AddModelError(nameof(model.Name), "That name already exists.");
            }
            return ModelState.IsValid;
        }

        private static T NewEntity<T>() where T : class
            => (T?)Activator.CreateInstance(typeof(T))
                ?? throw new InvalidOperationException($"Could not construct {typeof(T).Name}.");

        private static void ApplyForm<T>(T item, VariantOptionFormViewModel form) where T : class
        {
            var label = LabelProperty(typeof(T))
                ?? throw new InvalidOperationException($"{typeof(T).Name} has no supported name field.");
            label.SetValue(item, form.Name);
            if (OrderProperty(typeof(T)) is { } order) order.SetValue(item, form.DisplayOrder);
            if (ActiveProperty(typeof(T)) is { } active) active.SetValue(item, form.IsActive);
            if (form.IsColor && HexProperty(typeof(T)) is { } hex)
                hex.SetValue(item, form.HexCode);
        }

        private void HandleSaveError(Exception ex, string operation)
        {
            _logger.LogError(ex, "Could not {Operation}", operation);
            ModelState.AddModelError("", _env.IsDevelopment()
                ? ex.GetBaseException().Message
                : "Database could not save changes. Please check application logs.");
        }
    }
}
