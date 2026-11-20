using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public sealed class SettingsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<SettingsController> _logger;

        private const string UploadUrlPrefix = "/uploads/settings/";
        private const long MaxLogoSize = 3 * 1024 * 1024;
        private const long MaxFaviconSize = 1 * 1024 * 1024;

        public SettingsController(ApplicationDbContext db,
            IWebHostEnvironment environment,
            ILogger<SettingsController> logger)
        {
            _db = db;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var values = await ReadSettingsAsync();
            return View(MapToViewModel(values));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> Index(StoreSettingsViewModel model)
        {
            var oldValues = await ReadSettingsAsync();
            model.LogoUrl = Get(oldValues, "branding.logo");
            model.FaviconUrl = Get(oldValues, "branding.favicon");

            // Normalize before model validation; the attributes still validate after binding.
            model.CurrencyCode = (model.CurrencyCode ?? "").Trim().ToUpperInvariant();
            model.ManualBkashNumber = string.IsNullOrWhiteSpace(model.ManualBkashNumber)
                ? null : model.ManualBkashNumber.Trim();
            model.ManualBkashInstructions = string.IsNullOrWhiteSpace(model.ManualBkashInstructions)
                ? null : model.ManualBkashInstructions.Trim();
            ModelState.Remove(nameof(model.CurrencyCode));
            // Re-validate normalized currency and all other properties.
            TryValidateModel(model);

            if (model.RemoveLogo && model.LogoFile != null)
                ModelState.AddModelError(nameof(model.LogoFile), "Choose either remove or replace, not both.");
            if (model.RemoveFavicon && model.FaviconFile != null)
                ModelState.AddModelError(nameof(model.FaviconFile), "Choose either remove or replace, not both.");

            await ValidateImageAsync(model.LogoFile, nameof(model.LogoFile), MaxLogoSize, allowIco: false);
            await ValidateImageAsync(model.FaviconFile, nameof(model.FaviconFile), MaxFaviconSize, allowIco: true);

            if (!ModelState.IsValid)
                return View(model);

            var writtenFiles = new List<string>();
            string? uploadedLogo = null;
            string? uploadedFavicon = null;

            try
            {
                if (model.LogoFile != null)
                {
                    uploadedLogo = await SaveImageAsync(model.LogoFile);
                    writtenFiles.Add(uploadedLogo);
                }
                if (model.FaviconFile != null)
                {
                    uploadedFavicon = await SaveImageAsync(model.FaviconFile);
                    writtenFiles.Add(uploadedFavicon);
                }

                // Existing keyed database table, no schema change required.
                var rows = (await _db.SiteSettings.ToListAsync())
                    .ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

                void Set(string key, string? value, string description)
                {
                    var normalized = value?.Trim() ?? "";
                    if (rows.TryGetValue(key, out var existing))
                    {
                        if (existing.Value != normalized)
                        {
                            existing.Value = normalized;
                            existing.UpdatedAt = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        var entry = new SiteSettings
                        {
                            Key = key,
                            Value = normalized,
                            Description = description,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.SiteSettings.Add(entry);
                        rows.Add(key, entry);
                    }
                }

                Set("general.store_name", model.StoreName, "Public-facing store name");
                Set("general.tagline", model.Tagline, "Short brand tagline");
                Set("general.currency", model.CurrencyCode, "Three-letter currency code");
                Set("general.country", model.Country, "Store country");
                Set("branding.accent_color", model.AccentColor, "Storefront accent hex color");

                if (uploadedLogo != null || model.RemoveLogo)
                    Set("branding.logo", uploadedLogo, "Uploaded store logo URL");
                if (uploadedFavicon != null || model.RemoveFavicon)
                    Set("branding.favicon", uploadedFavicon, "Uploaded favicon URL");

                Set("contact.email", model.SupportEmail, "Support email address");
                Set("contact.phone", model.ContactPhone, "Support phone number");
                Set("contact.address", model.StoreAddress, "Store business address");
                Set("contact.whatsapp", model.WhatsAppNumber, "WhatsApp number");

                Set("operations.maintenance", Bool(model.IsMaintenanceMode), "Storefront maintenance preference");
                Set("operations.cash_on_delivery", Bool(model.AllowCashOnDelivery), "Checkout cash on delivery preference");
                Set("payments.bkash_number", model.ManualBkashNumber, "Manual bKash receiving account number; empty disables new bKash checkout");
                Set("payments.bkash_instructions", model.ManualBkashInstructions, "Instructions displayed alongside manual bKash payment details");
                Set("operations.order_notifications", Bool(model.SendOrderNotifications), "Order notification preference");
                Set("shipping.standard_fee", Money(model.StandardShippingFee), "Standard delivery fee");
                Set("shipping.free_threshold", Money(model.FreeShippingThreshold), "Spend threshold for free delivery (0 disables)");

                Set("social.facebook", model.FacebookUrl, "Facebook URL");
                Set("social.instagram", model.InstagramUrl, "Instagram URL");
                Set("social.tiktok", model.TikTokUrl, "TikTok URL");
                Set("social.youtube", model.YouTubeUrl, "YouTube URL");
                Set("social.x", model.XUrl, "X profile URL");

                Set("seo.title", model.SeoTitle, "Home page meta title");
                Set("seo.description", model.SeoDescription, "Home page meta description");
                Set("seo.keywords", model.SeoKeywords, "Home page meta keywords");

                await _db.SaveChangesAsync();

                if (uploadedLogo != null || model.RemoveLogo)
                    DeleteOwnedUpload(model.LogoUrl);
                if (uploadedFavicon != null || model.RemoveFavicon)
                    DeleteOwnedUpload(model.FaviconUrl);

                TempData["SettingsSuccess"] = "Store settings saved successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                foreach (var path in writtenFiles)
                    DeleteOwnedUpload(path);
                _logger.LogError(ex, "Failed to save Jersey Ecommerce settings.");
                ModelState.AddModelError("", "Settings could not be saved. Please retry.");
                return View(model);
            }
        }

        private static string Bool(bool flag) => flag ? "true" : "false";
        private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private async Task<Dictionary<string, string?>> ReadSettingsAsync()
        {
            var data = await _db.SiteSettings.AsNoTracking().ToListAsync();
            return data.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static string Get(Dictionary<string, string?> s, string key, string fallback = "") =>
            s.TryGetValue(key, out var value) ? value ?? fallback : fallback;

        private static bool GetBool(Dictionary<string, string?> s, string key, bool fallback = false) =>
            bool.TryParse(Get(s, key), out var value) ? value : fallback;

        private static decimal GetMoney(Dictionary<string, string?> s, string key)
        {
            var text = Get(s, key);
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value : 0m;
        }

        private static StoreSettingsViewModel MapToViewModel(Dictionary<string, string?> s) => new()
        {
            StoreName = Get(s, "general.store_name", "PANENKA"),
            Tagline = Get(s, "general.tagline"),
            CurrencyCode = Get(s, "general.currency", "BDT"),
            Country = Get(s, "general.country", "Bangladesh"),
            AccentColor = Get(s, "branding.accent_color", "#111111"),
            LogoUrl = Get(s, "branding.logo"),
            FaviconUrl = Get(s, "branding.favicon"),
            SupportEmail = Get(s, "contact.email"),
            ContactPhone = Get(s, "contact.phone"),
            StoreAddress = Get(s, "contact.address"),
            WhatsAppNumber = Get(s, "contact.whatsapp"),
            IsMaintenanceMode = GetBool(s, "operations.maintenance"),
            AllowCashOnDelivery = GetBool(s, "operations.cash_on_delivery", true),
            ManualBkashNumber = Get(s, "payments.bkash_number"),
            ManualBkashInstructions = Get(s, "payments.bkash_instructions",
                "Send Money to the receiving number shown, then submit your sender number, transaction ID and payment time."),
            SendOrderNotifications = GetBool(s, "operations.order_notifications", true),
            StandardShippingFee = GetMoney(s, "shipping.standard_fee"),
            FreeShippingThreshold = GetMoney(s, "shipping.free_threshold"),
            FacebookUrl = Get(s, "social.facebook"),
            InstagramUrl = Get(s, "social.instagram"),
            TikTokUrl = Get(s, "social.tiktok"),
            YouTubeUrl = Get(s, "social.youtube"),
            XUrl = Get(s, "social.x"),
            SeoTitle = Get(s, "seo.title"),
            SeoDescription = Get(s, "seo.description"),
            SeoKeywords = Get(s, "seo.keywords")
        };

        private async Task ValidateImageAsync(IFormFile? file, string property, long maxSize, bool allowIco)
        {
            if (file == null) return;
            if (file.Length <= 0 || file.Length > maxSize)
            {
                ModelState.AddModelError(property, $"Choose an image smaller than {maxSize / 1024 / 1024} MB.");
                return;
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" &&
                extension != ".webp" && !(allowIco && extension == ".ico"))
            {
                ModelState.AddModelError(property, allowIco ? "Use PNG, JPG, WEBP, or ICO." : "Use PNG, JPG, or WEBP.");
                return;
            }

            // Validate file signature as well as extension to reject disguised uploads.
            var header = new byte[12];
            int bytesRead;
            using (var input = file.OpenReadStream())
                bytesRead = await input.ReadAsync(header, 0, header.Length);

            bool valid = extension switch
            {
                ".png" => bytesRead >= 8 && header[0] == 137 && header[1] == 80 &&
                    header[2] == 78 && header[3] == 71 && header[4] == 13 &&
                    header[5] == 10 && header[6] == 26 && header[7] == 10,
                ".jpg" or ".jpeg" => bytesRead >= 3 && header[0] == 0xFF &&
                    header[1] == 0xD8 && header[2] == 0xFF,
                ".webp" => bytesRead >= 12 && header[0] == 'R' && header[1] == 'I' &&
                    header[2] == 'F' && header[3] == 'F' && header[8] == 'W' &&
                    header[9] == 'E' && header[10] == 'B' && header[11] == 'P',
                ".ico" => bytesRead >= 4 && header[0] == 0 && header[1] == 0 &&
                    header[2] == 1 && header[3] == 0,
                _ => false
            };

            if (!valid)
                ModelState.AddModelError(property, "This does not appear to be a valid image file.");
        }

        private string RootPath => _environment.WebRootPath ??
            Path.Combine(_environment.ContentRootPath, "wwwroot");

        private async Task<string> SaveImageAsync(IFormFile file)
        {
            var folder = Path.Combine(RootPath, "uploads", "settings");
            Directory.CreateDirectory(folder);
            var fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
            var fullPath = Path.Combine(folder, fileName);

            await using (var destination = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await file.CopyToAsync(destination);

            return UploadUrlPrefix + fileName;
        }

        private void DeleteOwnedUpload(string? savedUrl)
        {
            if (string.IsNullOrWhiteSpace(savedUrl) ||
                !savedUrl.StartsWith(UploadUrlPrefix, StringComparison.Ordinal))
                return;

            // Never delete an arbitrary path; only local generated files under settings.
            var fileName = Path.GetFileName(savedUrl);
            if (fileName.Contains('/') || fileName.Contains('\\') || fileName == "." || fileName == "..")
                return;
            try
            {
                var path = Path.Combine(RootPath, "uploads", "settings", fileName);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete an outdated settings image.");
            }
        }
    }
}
