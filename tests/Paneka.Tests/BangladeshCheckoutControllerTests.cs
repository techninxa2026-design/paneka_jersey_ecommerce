using System.Security.Claims;
using System.Text.Json;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Controllers;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Content;
using Jersey_ecommerce.Models.Shopping;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Services.Checkout;
using Jersey_ecommerce.ViewModels.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Color = Jersey_ecommerce.Models.Catalog.Color;

namespace Paneka.Tests;

public sealed class BangladeshCheckoutControllerTests
{
    [Fact]
    public async Task Payment_modal_prepare_route_reserves_one_order_and_receipt_submission_updates_it()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new ApplicationUser { Id = "buyer", UserName = "buyer@example.test" });
        db.Categories.Add(new Category { Id = 1, Name = "Jerseys", Slug = "jerseys" });
        db.Sizes.Add(new Size { Id = 1, Name = "M", IsActive = true });
        db.Colors.Add(new Color { Id = 1, Name = "Black", IsActive = true });
        db.Products.Add(new Product { Id = 1, Name = "Home Jersey", Slug = "home-jersey",
            CategoryId = 1, BasePrice = 1200m, IsPublished = true });
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1,
            SizeId = 1, ColorId = 1, SKU = "HOME-M", StockQuantity = 7,
            Price = 1200m, IsActive = true });
        db.ShoppingCarts.Add(new ShoppingCart { Id = 1, UserId = "buyer" });
        db.CartItems.Add(new CartItem { ShoppingCartId = 1, ProductVariantId = 1, Quantity = 2 });
        db.SiteSettings.Add(new SiteSettings
        {
            Key = ManualBkashPaymentService.NumberSettingKey, Value = "01712345678"
        });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "buyer")
        }, "test"));
        http.Features.Set<ISessionFeature>(new InMemorySessionFeature());
        var delivery = new DeliveryInput
        {
            FullName = "Buyer", Email = "buyer@example.test", Phone = "01912345678",
            AddressLine1 = "Village road", City = "Kaliganj",
            District = "Gazipur", Region = "OutsideDhaka"
        };
        http.Session.SetString("PANENKA_DELIVERY_V2_buyer", JsonSerializer.Serialize(delivery));
        var token = Guid.NewGuid().ToString("N");
        http.Session.SetString("PANENKA_ORDER_TOKEN_V2_buyer", token);
        var accessor = new HttpContextAccessor { HttpContext = http };
        var checkout = new CheckoutService(db, Options.Create(new CheckoutOptions()),
            accessor, new InventoryStockService(db));
        var controller = new CheckoutController(checkout, db, null!,
            NullLogger<CheckoutController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new TestTempDataProvider());

        var bad = await controller.PrepareBkash(token, "01799999999");
        Assert.IsType<BadRequestObjectResult>(bad);
        Assert.Empty(await db.Orders.AsNoTracking().ToListAsync());

        var result = Assert.IsType<OkObjectResult>(await controller.PrepareBkash(
            token, "01712345678"));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var id = body.RootElement.GetProperty("orderId").GetInt32();
        Assert.Equal("01712345678", body.RootElement.GetProperty("recipientNumber").GetString());
        Assert.Equal("Pending", body.RootElement.GetProperty("paymentStatus").GetString());
        Assert.Equal(2400m, body.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("Gazipur", (await db.Orders.AsNoTracking().SingleAsync()).ShippingDistrict);
        Assert.Equal(5, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);

        // Repeated fetch by a slow mobile connection resumes exactly the saved order.
        var again = Assert.IsType<OkObjectResult>(await controller.PrepareBkash(
            token, "01712345678"));
        using var duplicate = JsonDocument.Parse(JsonSerializer.Serialize(again.Value));
        Assert.Equal(id, duplicate.RootElement.GetProperty("orderId").GetInt32());
        Assert.Single(await db.Orders.AsNoTracking().ToListAsync());
        Assert.Equal(5, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);

        var paymentController = new BkashController(db, new ManualBkashPaymentService(db),
            NullLogger<BkashController>.Instance);
        paymentController.ControllerContext = new ControllerContext { HttpContext = http };
        paymentController.TempData = new TempDataDictionary(http, new TestTempDataProvider());
        var confirmation = await paymentController.Submit(id, "01912345678",
            "QW12345678", DateTime.UtcNow.AddHours(6));
        Assert.IsType<RedirectToActionResult>(confirmation);
        var payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Submitted", payment.Status);
        Assert.Equal("QW12345678", payment.TransactionReference);
        Assert.NotNull(payment.ManualSubmittedAt);
        Assert.Null(payment.PaidAt);
        Assert.Equal("Pending", (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await db.PaymentTransactions.AsNoTracking().ToListAsync());
    }

    private sealed class InMemorySessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = new InMemorySession();
    }
    private sealed class InMemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new();
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => values.TryGetValue(key, out value!);
    }
    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
