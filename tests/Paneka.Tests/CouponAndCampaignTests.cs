using System.Text.Json;
using Jersey_ecommerce.Areas.Admin.Controllers;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Controllers;
using Jersey_ecommerce.Controllers.Api;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Checkout;
using Jersey_ecommerce.Models.Content;
using Jersey_ecommerce.Models.Promotions;
using Jersey_ecommerce.Models.Shopping;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Services.Checkout;
using Jersey_ecommerce.ViewModels.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Color = Jersey_ecommerce.Models.Catalog.Color;

namespace Paneka.Tests;

public sealed class CouponAndCampaignTests
{
    [Fact]
    public async Task Coupon_is_applied_to_real_order_once_and_released_on_cancellation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "buyer", UserName = "buyer@example.test" });
        db.Categories.Add(new Category { Id = 1, Name = "Football Jerseys", Slug = "football-jerseys" });
        db.Sizes.Add(new Size { Id = 1, Name = "M", IsActive = true });
        db.Colors.Add(new Color { Id = 1, Name = "Black", IsActive = true });
        db.Products.Add(new Product { Id = 1, Name = "Home Shirt", Slug = "home-shirt",
            CategoryId = 1, IsPublished = true, BasePrice = 1500m });
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1, SizeId = 1,
            ColorId = 1, IsActive = true, SKU = "HOME-M", Price = 1500m, StockQuantity = 5 });
        db.ShoppingCarts.Add(new ShoppingCart { Id = 1, UserId = "buyer" });
        db.CartItems.Add(new CartItem { ShoppingCartId = 1, ProductVariantId = 1, Quantity = 2 });
        db.Coupons.Add(new Coupon { Id = 1, Code = "MATCH10",
            DiscountType = "Percentage", DiscountValue = 10m, IsActive = true,
            UsageLimit = 1, UsageLimitPerUser = 1 });
        db.Coupons.Add(new Coupon { Id = 2, Code = "EXPIRED", DiscountType = "Fixed",
            DiscountValue = 500m, ExpiresAt = DateTime.UtcNow.AddDays(-1), IsActive = true });
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Features.Set<ISessionFeature>(new MemorySessionFeature());
        var accessor = new HttpContextAccessor { HttpContext = context };
        var stock = new InventoryStockService(db);
        var checkout = new CheckoutService(db, Options.Create(new CheckoutOptions()), accessor, stock);
        await Assert.ThrowsAsync<CheckoutException>(() => checkout.SetCoupon("buyer", "EXPIRED"));
        await checkout.SetCoupon("buyer", "MATCH10");

        var cart = await checkout.GetCart("buyer");
        Assert.Equal(3000m, cart.Subtotal);
        Assert.Equal(300m, cart.DiscountAmount);
        Assert.Equal("MATCH10", cart.PromoCode);

        var delivery = new DeliveryInput { FullName = "Buyer", Email = "buyer@example.test",
            Phone = "01700000000", Region = "Dhaka", District = "Dhaka",
            City = "Dhaka", AddressLine1 = "Main road" };
        var input = new PlaceOrderInput { PaymentMethod = "COD", SubmissionToken = Guid.NewGuid().ToString("N") };
        var orderId = await checkout.PlaceOrder("buyer", delivery, input);

        var order = await db.Orders.SingleAsync();
        Assert.Equal(2700m, order.TotalAmount);
        Assert.Equal(300m, order.DiscountAmount);
        Assert.Equal("MATCH10", order.CouponCode);
        Assert.Equal(1, (await db.Coupons.SingleAsync(c => c.Id == 1)).UsageCount);
        Assert.Single(await db.CouponUsages.ToListAsync());
        await Assert.ThrowsAsync<CheckoutException>(() => checkout.SetCoupon("buyer", "MATCH10"));

        await new OrderCancellationService(db, stock).CancelUnpaidPendingOrder(orderId, "buyer");
        Assert.Equal(0, (await db.Coupons.SingleAsync(c => c.Id == 1)).UsageCount);
        Assert.Empty(await db.CouponUsages.ToListAsync());
        Assert.Equal(5, (await db.ProductVariants.SingleAsync()).StockQuantity);
    }

    [Fact]
    public async Task Scheduled_marketing_is_visible_in_the_correct_customer_placements()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;
        db.Banners.AddRange(
            new Banner { Title = "Shop promotion", ImageUrl = "/uploads/marketing/banners/shop.webp",
                Placement = "Shop", DisplayOrder = 2, IsActive = true },
            new Banner { Title = "Category promotion", ImageUrl = "/uploads/marketing/banners/category.webp",
                Placement = "Category", DisplayOrder = 1, IsActive = true },
            new Banner { Title = "Top promotion", ImageUrl = "/uploads/marketing/banners/top.webp",
                Placement = "HomeTop", IsActive = true },
            new Banner { Title = "Middle promotion", ImageUrl = "/uploads/marketing/banners/mid.webp",
                Placement = "HomeMiddle", IsActive = true },
            new Banner { Title = "Footer promotion", ImageUrl = "/uploads/marketing/banners/footer.webp",
                Placement = "Footer", IsActive = true },
            new Banner { Title = "Inactive promotion", ImageUrl = "/banners/inactive.webp",
                Placement = "Home", IsActive = false },
            new Banner { Title = "Expired promotion", ImageUrl = "/banners/expired.webp",
                Placement = "Home", IsActive = true, EndsAt = now.AddMinutes(-5) });
        await db.SaveChangesAsync();

        var shop = new ShopCatalogApiController(db);
        var unfiltered = Json(Assert.IsType<OkObjectResult>(await shop.ShopBanner()));
        Assert.Equal("Shop promotion", unfiltered.RootElement.GetProperty("title").GetString());
        var filtered = Json(Assert.IsType<OkObjectResult>(await shop.ShopBanner(categoryId: 1)));
        Assert.Equal("Category promotion", filtered.RootElement.GetProperty("title").GetString());

        var home = new HomeStorefrontApiController(db);
        var result = Json(Assert.IsType<OkObjectResult>(await home.Get(default)));
        var campaigns = result.RootElement.GetProperty("banners");
        var placements = campaigns.EnumerateArray().Select(x => x.GetProperty("placement").GetString()).ToHashSet();
        Assert.Contains("HomeTop", placements);
        Assert.Contains("HomeMiddle", placements);
        Assert.Contains("Footer", placements);
        Assert.DoesNotContain(campaigns.EnumerateArray(), x =>
            x.GetProperty("title").GetString() == "Inactive promotion" ||
            x.GetProperty("title").GetString() == "Expired promotion");
    }

    [Fact]
    public async Task Admin_cannot_create_an_unsupported_banner_placement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        var controller = new BannersController(db, null!, NullLogger<BannersController>.Instance);
        var result = await controller.CreateBanner(new BannerFormViewModel {
            Title = "Invalid campaign", Placement = "MissingStorefrontSlot"
        });
        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState[nameof(BannerFormViewModel.Placement)]!.Errors,
            x => x.ErrorMessage.Contains("supported banner placement"));
        Assert.Empty(await db.Banners.ToListAsync());
    }

    private static JsonDocument Json(OkObjectResult value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value.Value));

    private static ApplicationDbContext Database(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private sealed class MemorySessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = new InMemorySession();
    }

    private sealed class InMemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> data = new();
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public IEnumerable<string> Keys => data.Keys;
        public void Clear() => data.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => data.Remove(key);
        public void Set(string key, byte[] value) => data[key] = value;
        public bool TryGetValue(string key, out byte[] value) => data.TryGetValue(key, out value!);
    }
}
