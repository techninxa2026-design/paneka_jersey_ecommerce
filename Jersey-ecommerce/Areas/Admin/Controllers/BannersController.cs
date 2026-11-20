using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    [Route("Admin/Banners")]
    public sealed class BannersController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BannersController> _logger;

        private const long MaxImageBytes = 5 * 1024 * 1024;
        private const string UploadRoot = "uploads/marketing";

        private static readonly HashSet<string> ImageExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

        private static readonly TimeZoneInfo BangladeshTimeZone = FindBangladeshTimeZone();

        public BannersController(
            ApplicationDbContext db,
            IWebHostEnvironment env,
            ILogger<BannersController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // GET /Admin/Banners
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var vm = new BannersIndexViewModel
            {
                Slides = await _db.HeroSlides.AsNoTracking()
                    .OrderBy(s => s.DisplayOrder).ThenByDescending(s => s.Id)
                    .ToListAsync(),
                Banners = await _db.Banners.AsNoTracking()
                    .OrderBy(b => b.DisplayOrder).ThenByDescending(b => b.Id)
                    .ToListAsync()
            };
            return View(vm);
        }

        // =========================== HERO SLIDES ============================

        [HttpGet("CreateSlide")]
        public IActionResult CreateSlide() => View("SlideForm", new HeroSlideFormViewModel());

        [HttpPost("CreateSlide")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(13 * 1024 * 1024)]
        public async Task<IActionResult> CreateSlide(HeroSlideFormViewModel model)
        {
            ValidateSchedule(model.StartsAt, model.EndsAt);
            ValidateLink(model.ButtonUrl, nameof(model.ButtonUrl));
            await ValidateImageAsync(model.ImageFile, nameof(model.ImageFile), required: true);
            await ValidateImageAsync(model.MobileImageFile, nameof(model.MobileImageFile));

            if (!ModelState.IsValid) return View("SlideForm", model);

            string? desktop = null;
            string? mobile = null;
            try
            {
                desktop = await SaveImageAsync(model.ImageFile!, "slides");
                if (model.MobileImageFile is not null)
                    mobile = await SaveImageAsync(model.MobileImageFile, "slides");

                _db.HeroSlides.Add(new HeroSlide
                {
                    Title = (model.Title ?? string.Empty).Trim(),
                    Subtitle = Clean(model.Subtitle),
                    ImageUrl = desktop,
                    MobileImageUrl = mobile,
                    ButtonText = Clean(model.ButtonText),
                    ButtonUrl = Clean(model.ButtonUrl),
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive,
                    StartsAt = ToUtc(model.StartsAt),
                    EndsAt = ToUtc(model.EndsAt),
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                TempData["Success"] = "Hero slide created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create hero slide");
                DeleteUploadedFile(desktop, "slides");
                DeleteUploadedFile(mobile, "slides");
                ModelState.AddModelError(string.Empty, "Could not save the slide. Please try again.");
                return View("SlideForm", model);
            }
        }

        [HttpGet("EditSlide/{id:int}")]
        public async Task<IActionResult> EditSlide(int id)
        {
            var item = await _db.HeroSlides.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (item is null) return NotFound();
            return View("SlideForm", new HeroSlideFormViewModel
            {
                Id = item.Id,
                Title = item.Title,
                Subtitle = item.Subtitle,
                CurrentImageUrl = item.ImageUrl,
                CurrentMobileImageUrl = item.MobileImageUrl,
                ButtonText = item.ButtonText,
                ButtonUrl = item.ButtonUrl,
                DisplayOrder = item.DisplayOrder,
                IsActive = item.IsActive,
                StartsAt = ToBangladesh(item.StartsAt),
                EndsAt = ToBangladesh(item.EndsAt)
            });
        }

        [HttpPost("EditSlide")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(13 * 1024 * 1024)]
        public async Task<IActionResult> EditSlide(HeroSlideFormViewModel model)
        {
            var item = await _db.HeroSlides.FirstOrDefaultAsync(s => s.Id == model.Id);
            if (item is null) return NotFound();

            // Read current image URLs from database, never from client-posted hidden values.
            model.CurrentImageUrl = item.ImageUrl;
            model.CurrentMobileImageUrl = item.MobileImageUrl;

            ValidateSchedule(model.StartsAt, model.EndsAt);
            ValidateLink(model.ButtonUrl, nameof(model.ButtonUrl));
            await ValidateImageAsync(model.ImageFile, nameof(model.ImageFile));
            if (!model.RemoveMobileImage)
                await ValidateImageAsync(model.MobileImageFile, nameof(model.MobileImageFile));
            else if (model.MobileImageFile is not null)
                ModelState.AddModelError(nameof(model.MobileImageFile), "Choose either removal or a new mobile image.");

            if (!ModelState.IsValid) return View("SlideForm", model);

            string? newDesktop = null;
            string? newMobile = null;
            var oldDesktop = item.ImageUrl;
            var oldMobile = item.MobileImageUrl;
            try
            {
                if (model.ImageFile is not null)
                    newDesktop = await SaveImageAsync(model.ImageFile, "slides");
                if (model.MobileImageFile is not null)
                    newMobile = await SaveImageAsync(model.MobileImageFile, "slides");

                item.Title = (model.Title ?? string.Empty).Trim();
                item.Subtitle = Clean(model.Subtitle);
                item.ImageUrl = newDesktop ?? item.ImageUrl;
                item.MobileImageUrl = model.RemoveMobileImage ? null : newMobile ?? item.MobileImageUrl;
                item.ButtonText = Clean(model.ButtonText);
                item.ButtonUrl = Clean(model.ButtonUrl);
                item.DisplayOrder = model.DisplayOrder;
                item.IsActive = model.IsActive;
                item.StartsAt = ToUtc(model.StartsAt);
                item.EndsAt = ToUtc(model.EndsAt);

                await _db.SaveChangesAsync();
                if (newDesktop is not null) DeleteUploadedFile(oldDesktop, "slides");
                if (newMobile is not null || model.RemoveMobileImage)
                    DeleteUploadedFile(oldMobile, "slides");

                TempData["Success"] = "Hero slide updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update hero slide {Id}", model.Id);
                DeleteUploadedFile(newDesktop, "slides");
                DeleteUploadedFile(newMobile, "slides");
                ModelState.AddModelError(string.Empty, "Could not update the slide. Please try again.");
                return View("SlideForm", model);
            }
        }

        [HttpPost("ToggleSlide")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSlide(int id)
        {
            var item = await _db.HeroSlides.FindAsync(id);
            if (item is null) return NotFound();
            item.IsActive = !item.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = item.IsActive ? "Slide activated." : "Slide deactivated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("DeleteSlide")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSlide(int id)
        {
            var item = await _db.HeroSlides.FindAsync(id);
            if (item is null) return NotFound();
            var desktop = item.ImageUrl;
            var mobile = item.MobileImageUrl;
            _db.HeroSlides.Remove(item);
            await _db.SaveChangesAsync();
            DeleteUploadedFile(desktop, "slides");
            DeleteUploadedFile(mobile, "slides");
            TempData["Success"] = "Hero slide deleted.";
            return RedirectToAction(nameof(Index));
        }

        // ============================== BANNERS ==============================

        [HttpGet("CreateBanner")]
        public IActionResult CreateBanner() => View("BannerForm", new BannerFormViewModel());

        [HttpPost("CreateBanner")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(13 * 1024 * 1024)]
        public async Task<IActionResult> CreateBanner(BannerFormViewModel model)
        {
            ValidateSchedule(model.StartsAt, model.EndsAt);
            ValidateLink(model.LinkUrl, nameof(model.LinkUrl));
            await ValidateImageAsync(model.ImageFile, nameof(model.ImageFile), required: true);
            if (!ModelState.IsValid) return View("BannerForm", model);

            string? image = null;
            try
            {
                image = await SaveImageAsync(model.ImageFile!, "banners");
                _db.Banners.Add(new Banner
                {
                    Title = (model.Title ?? string.Empty).Trim(),
                    Subtitle = Clean(model.Subtitle),
                    ImageUrl = image,
                    LinkUrl = Clean(model.LinkUrl),
                    Placement = (model.Placement ?? string.Empty).Trim(),
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive,
                    StartsAt = ToUtc(model.StartsAt),
                    EndsAt = ToUtc(model.EndsAt),
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                TempData["Success"] = "Banner created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create banner");
                DeleteUploadedFile(image, "banners");
                ModelState.AddModelError(string.Empty, "Could not save the banner. Please try again.");
                return View("BannerForm", model);
            }
        }

        [HttpGet("EditBanner/{id:int}")]
        public async Task<IActionResult> EditBanner(int id)
        {
            var item = await _db.Banners.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
            if (item is null) return NotFound();
            return View("BannerForm", new BannerFormViewModel
            {
                Id = item.Id,
                Title = item.Title,
                Subtitle = item.Subtitle,
                CurrentImageUrl = item.ImageUrl,
                LinkUrl = item.LinkUrl,
                Placement = item.Placement,
                DisplayOrder = item.DisplayOrder,
                IsActive = item.IsActive,
                StartsAt = ToBangladesh(item.StartsAt),
                EndsAt = ToBangladesh(item.EndsAt)
            });
        }

        [HttpPost("EditBanner")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(13 * 1024 * 1024)]
        public async Task<IActionResult> EditBanner(BannerFormViewModel model)
        {
            var item = await _db.Banners.FirstOrDefaultAsync(b => b.Id == model.Id);
            if (item is null) return NotFound();
            model.CurrentImageUrl = item.ImageUrl;
            ValidateSchedule(model.StartsAt, model.EndsAt);
            ValidateLink(model.LinkUrl, nameof(model.LinkUrl));
            await ValidateImageAsync(model.ImageFile, nameof(model.ImageFile));
            if (!ModelState.IsValid) return View("BannerForm", model);

            string? newImage = null;
            var oldImage = item.ImageUrl;
            try
            {
                if (model.ImageFile is not null)
                    newImage = await SaveImageAsync(model.ImageFile, "banners");

                item.Title = (model.Title ?? string.Empty).Trim();
                item.Subtitle = Clean(model.Subtitle);
                item.ImageUrl = newImage ?? item.ImageUrl;
                item.LinkUrl = Clean(model.LinkUrl);
                item.Placement = (model.Placement ?? string.Empty).Trim();
                item.DisplayOrder = model.DisplayOrder;
                item.IsActive = model.IsActive;
                item.StartsAt = ToUtc(model.StartsAt);
                item.EndsAt = ToUtc(model.EndsAt);

                await _db.SaveChangesAsync();
                if (newImage is not null) DeleteUploadedFile(oldImage, "banners");
                TempData["Success"] = "Banner updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update banner {Id}", model.Id);
                DeleteUploadedFile(newImage, "banners");
                ModelState.AddModelError(string.Empty, "Could not update the banner. Please try again.");
                return View("BannerForm", model);
            }
        }

        [HttpPost("ToggleBanner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBanner(int id)
        {
            var item = await _db.Banners.FindAsync(id);
            if (item is null) return NotFound();
            item.IsActive = !item.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = item.IsActive ? "Banner activated." : "Banner deactivated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("DeleteBanner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBanner(int id)
        {
            var item = await _db.Banners.FindAsync(id);
            if (item is null) return NotFound();
            var image = item.ImageUrl;
            _db.Banners.Remove(item);
            await _db.SaveChangesAsync();
            DeleteUploadedFile(image, "banners");
            TempData["Success"] = "Banner deleted.";
            return RedirectToAction(nameof(Index));
        }

        // =========================== VALIDATION / FILES ======================

        private void ValidateSchedule(DateTime? startsAt, DateTime? endsAt)
        {
            if (startsAt.HasValue && endsAt.HasValue && endsAt.Value <= startsAt.Value)
                ModelState.AddModelError("EndsAt", "The end date must be later than the start date.");
        }

        private void ValidateLink(string? link, string field)
        {
            if (string.IsNullOrWhiteSpace(link)) return;
            var url = link.Trim();
            var relative = (url.StartsWith('/') && !url.StartsWith("//") && !url.Contains('\\')) ||
                           (url.StartsWith('#') && !url.Contains('\\'));
            var absolute = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            if (!relative && !absolute)
                ModelState.AddModelError(field, "Enter a safe URL starting with /, #, http:// or https://.");
        }

        private async Task ValidateImageAsync(IFormFile? image, string field, bool required = false)
        {
            if (image is null)
            {
                if (required) ModelState.AddModelError(field, "Choose an image to upload.");
                return;
            }

            if (image.Length == 0 || image.Length > MaxImageBytes)
            {
                ModelState.AddModelError(field, "Image must be between 1 byte and 5 MB.");
                return;
            }

            var ext = Path.GetExtension(image.FileName);
            if (!ImageExtensions.Contains(ext))
            {
                ModelState.AddModelError(field, "Only JPG, PNG or WebP images are supported.");
                return;
            }

            byte[] header = new byte[12];
            await using var stream = image.OpenReadStream();
            var read = 0;
            while (read < header.Length)
            {
                var n = await stream.ReadAsync(header.AsMemory(read, header.Length - read));
                if (n == 0) break;
                read += n;
            }

            var validJpeg = read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            var validPng = read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10});
            var validWebp = read >= 12 &&
                header.AsSpan(0, 4).SequenceEqual(new byte[] { 82, 73, 70, 70 }) &&
                header.AsSpan(8, 4).SequenceEqual(new byte[] { 87, 69, 66, 80 });

            bool matches = ext.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => validJpeg,
                ".png" => validPng,
                ".webp" => validWebp,
                _ => false
            };
            if (!matches)
                ModelState.AddModelError(field, "The uploaded file is not a valid image of its selected type.");
        }

        private async Task<string> SaveImageAsync(IFormFile image, string kind)
        {
            // kind comes only from hard-coded calls above (slides/banners).
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "uploads", "marketing", kind);
            Directory.CreateDirectory(folder);

            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            var filename = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(folder, filename);
            try
            {
                await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
                await image.CopyToAsync(output);
            }
            catch
            {
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                throw;
            }
            return $"/{UploadRoot}/{kind}/{filename}";
        }

        private void DeleteUploadedFile(string? imageUrl, string kind)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return;
            var allowedPrefix = $"/{UploadRoot}/{kind}/";
            if (!imageUrl.StartsWith(allowedPrefix, StringComparison.Ordinal)) return;
            var filename = imageUrl[allowedPrefix.Length..];
            if (filename.Length == 0 || filename != Path.GetFileName(filename)) return;

            try
            {
                var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                var path = Path.Combine(webRoot, "uploads", "marketing", kind, filename);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (Exception ex)
            {
                // Database operation succeeded; do not fail the request for an orphaned file.
                _logger.LogWarning(ex, "Unable to remove marketing image {ImageUrl}", imageUrl);
            }
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static TimeZoneInfo FindBangladeshTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka"); }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Bangladesh Standard Time");
            }
        }

        private static DateTime? ToUtc(DateTime? bangladeshLocal) =>
            bangladeshLocal.HasValue
                ? TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(bangladeshLocal.Value, DateTimeKind.Unspecified),
                    BangladeshTimeZone)
                : null;

        private static DateTime? ToBangladesh(DateTime? utc) =>
            utc.HasValue
                ? TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc),
                    BangladeshTimeZone)
                : null;
    }
}
