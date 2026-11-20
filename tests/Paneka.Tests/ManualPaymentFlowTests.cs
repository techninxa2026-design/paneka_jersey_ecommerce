using System.Security.Claims;
using Jersey_ecommerce.Areas.Admin.Controllers;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Controllers;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Checkout;
using Jersey_ecommerce.Models.Content;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Models.Shopping;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Services.Checkout;
using Jersey_ecommerce.ViewModels.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Color = Jersey_ecommerce.Models.Catalog.Color;

namespace Paneka.Tests;

public sealed class ManualPaymentFlowTests
{
    [Fact]
    public void Additive_manual_bkash_payment_migration_is_discoverable()
    {
        using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
        Assert.Contains("20261120120000_ManualBkashPayments", db.Database.GetMigrations());
    }

    [Fact]
    public async Task Manual_bkash_checkout_records_customer_claim_then_only_staff_marks_paid()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        db.SiteSettings.Add(new SiteSettings { Key = ManualBkashPaymentService.NumberSettingKey, Value = "01712345678" });
        await db.SaveChangesAsync();
        var checkout = Checkout(db);
        var orderId = await checkout.PlaceOrder("buyer", Delivery(), new PlaceOrderInput {
            PaymentMethod = "Bkash", SubmissionToken = Guid.NewGuid().ToString("N")
        });

        var payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Bkash", payment.Method);
        Assert.Equal("Pending", payment.Status);
        Assert.Equal("01712345678", payment.ManualRecipientNumber);
        Assert.Null(payment.TransactionReference);
        Assert.Null(payment.PaidAt);

        // Admin switches receiving wallets after this order is placed. Its audit
        // reference must remain the wallet the customer was instructed to use.
        var setting = await db.SiteSettings.SingleAsync();
        setting.Value = "01812345678";
        await db.SaveChangesAsync();

        var service = new ManualBkashPaymentService(db);
        await Assert.ThrowsAsync<CheckoutException>(() =>
            service.SubmitAsync(orderId, "other-buyer", "01912345678", "BE12345678", BangladeshNow()));
        await Assert.ThrowsAsync<CheckoutException>(() =>
            service.SubmitAsync(orderId, "buyer", "123", "BE12345678", BangladeshNow()));
        await Assert.ThrowsAsync<CheckoutException>(() =>
            service.SubmitAsync(orderId, "buyer", "01912345678", "BE12345678",
                BangladeshNow().AddDays(1)));

        await service.SubmitAsync(orderId, "buyer", "01912345678", "BE12345678", BangladeshNow());
        await service.SubmitAsync(orderId, "buyer", "01912345678", "BE12345678", BangladeshNow());
        payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Submitted", payment.Status);
        Assert.Equal("01912345678", payment.ManualSenderNumber);
        Assert.Equal("01712345678", payment.ManualRecipientNumber);
        Assert.Equal("BE12345678", payment.TransactionReference);
        Assert.NotNull(payment.ManualClaimedPaidAt);
        Assert.NotNull(payment.ManualSubmittedAt);
        Assert.Null(payment.PaidAt);
        Assert.Single(await db.PaymentTransactions.AsNoTracking().ToListAsync());
        Assert.Equal("Pending", (await db.Orders.AsNoTracking().SingleAsync()).Status);

        var customer = Setup(new BkashController(db, service, NullLogger<BkashController>.Instance), "buyer");
        var page = Assert.IsType<ViewResult>(await customer.Status(orderId));
        var receipt = Assert.IsType<ManualBkashStatusModel>(page.Model);
        Assert.Equal("01712345678", receipt.RecipientNumber);
        Assert.Equal("Submitted", receipt.PaymentStatus);
        Assert.False(receipt.CanSubmit);
        Assert.IsType<NotFoundResult>(await Setup(
            new BkashController(db, service, NullLogger<BkashController>.Instance),
            "other-buyer").Status(orderId));

        var admin = Setup(new CheckoutPaymentReviewController(db, service), "staff-1");
        await admin.Confirm(payment.Id, "Wallet transaction independently matched", independentlyVerified: false);
        Assert.Equal("Submitted", (await db.Payments.AsNoTracking().SingleAsync()).Status);

        await admin.Confirm(payment.Id, "Wallet transaction independently matched at merchant", independentlyVerified: true);
        payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Paid", payment.Status);
        Assert.NotNull(payment.PaidAt);
        Assert.Equal("staff-1", payment.ManualReviewedByUserId);
        Assert.NotNull(payment.ManualReviewedAt);
        Assert.Equal("Confirmed", (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(2, await db.PaymentTransactions.AsNoTracking().CountAsync());
        Assert.Contains(await db.PaymentTransactions.AsNoTracking().ToListAsync(), x => x.Status == "Verified");

        await Assert.ThrowsAsync<CheckoutException>(() =>
            service.VerifyAsync(payment.Id, "staff-1", "Second attempt is forbidden"));
        Assert.Equal(2, await db.PaymentTransactions.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Manual_bkash_rejection_keeps_order_unpaid_and_allows_corrected_resubmission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        db.SiteSettings.Add(new SiteSettings { Key = ManualBkashPaymentService.NumberSettingKey, Value = "01712345678" });
        await db.SaveChangesAsync();
        var orderId = await Checkout(db).PlaceOrder("buyer", Delivery(), new PlaceOrderInput {
            PaymentMethod = "Bkash", SubmissionToken = Guid.NewGuid().ToString("N")
        });
        var paymentId = (await db.Payments.AsNoTracking().SingleAsync()).Id;
        var service = new ManualBkashPaymentService(db);
        await service.SubmitAsync(orderId, "buyer", "01912345678", "BE00000001", BangladeshNow());
        await service.RejectAsync(paymentId, "staff-2", "Transaction not received in merchant wallet.");
        var rejected = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Rejected", rejected.Status);
        Assert.Null(rejected.PaidAt);
        Assert.Equal("Pending", (await db.Orders.AsNoTracking().SingleAsync()).Status);
        var status = Assert.IsType<ManualBkashStatusModel>(Assert.IsType<ViewResult>(
            await Setup(new BkashController(db, service, NullLogger<BkashController>.Instance),"buyer")
                .Status(orderId)).Model);
        Assert.True(status.CanSubmit);
        Assert.NotNull(status.ReviewNote);

        await service.SubmitAsync(orderId, "buyer", "01912345678", "BE00000002", BangladeshNow());
        Assert.Equal("Submitted", (await db.Payments.AsNoTracking().SingleAsync()).Status);
        await service.VerifyAsync(paymentId, "staff-2", "Verified corrected transfer against the wallet.");
        Assert.Equal("Paid", (await db.Payments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(4, await db.PaymentTransactions.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Removing_merchant_number_disables_new_bkash_orders_but_not_cod()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        await db.SaveChangesAsync();
        var checkout = Checkout(db);
        Assert.Null(await checkout.ManualBkashNumber());
        await Assert.ThrowsAsync<CheckoutException>(() =>
            checkout.PlaceOrder("buyer", Delivery(), new PlaceOrderInput {
                PaymentMethod = "Bkash", SubmissionToken = Guid.NewGuid().ToString("N")
            }));
        Assert.Empty(await db.Orders.AsNoTracking().ToListAsync());
        var cod = await checkout.PlaceOrder("buyer", Delivery(), new PlaceOrderInput {
            PaymentMethod = "COD", SubmissionToken = Guid.NewGuid().ToString("N")
        });
        Assert.True(cod > 0);
        Assert.Equal("CashOnDelivery", (await db.Payments.AsNoTracking().SingleAsync()).Method);
    }

    [Fact]
    public async Task Duplicate_bkash_trx_id_is_rejected_for_a_different_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        db.SiteSettings.Add(new SiteSettings { Key = ManualBkashPaymentService.NumberSettingKey, Value = "01712345678" });
        await db.SaveChangesAsync();

        var orderId = await Checkout(db).PlaceOrder("buyer", Delivery(), new PlaceOrderInput {
            PaymentMethod = "Bkash", SubmissionToken = Guid.NewGuid().ToString("N")
        });
        var service = new ManualBkashPaymentService(db);
        await service.SubmitAsync(orderId, "buyer", "01912345678", "AB12345678", BangladeshNow());

        db.Orders.Add(new Order { UserId = "other-buyer", OrderNumber = "PNK-OTHER",
            Status = "Pending", CustomerName = "Other", CustomerEmail = "other@example.test",
            CustomerPhone = "01800000000", ShippingAddressLine1 = "Test",
            ShippingCity = "Dhaka", ShippingCountry = "Bangladesh", TotalAmount = 900m });
        await db.SaveChangesAsync();
        var second = await db.Orders.Where(x=>x.UserId=="other-buyer").SingleAsync();
        db.Payments.Add(new Payment { OrderId = second.Id, Method = "Bkash",
            ManualRecipientNumber = "01712345678", Amount = 900m, Status = "Pending" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<CheckoutException>(() =>
            service.SubmitAsync(second.Id, "other-buyer", "01912345678", "AB12345678", BangladeshNow()));
        Assert.Equal("Pending", (await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == second.Id)).Status);

        // Replacing a rejected customer claim must not make its old TrxID
        // reusable for someone else's order.
        var originalPaymentId = (await db.Payments.AsNoTracking()
            .SingleAsync(x => x.OrderId == orderId)).Id;
        await service.RejectAsync(originalPaymentId, "staff-1", "No matching wallet transfer was received.");
        await service.SubmitAsync(orderId, "buyer", "01912345678", "AB12345679", BangladeshNow());
        await Assert.ThrowsAsync<CheckoutException>(() =>
            service.SubmitAsync(second.Id, "other-buyer", "01912345678", "AB12345678", BangladeshNow()));
    }

    [Fact]
    public async Task Cash_on_delivery_paid_status_requires_delivered_order_and_independent_staff_confirmation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        await db.SaveChangesAsync();
        var id = await Checkout(db).PlaceOrder("buyer", Delivery(), new PlaceOrderInput {
            PaymentMethod = "COD", SubmissionToken = Guid.NewGuid().ToString("N")
        });
        var paymentId = (await db.Payments.AsNoTracking().SingleAsync()).Id;
        var admin = Setup(new PaymentsController(db), "staff-1");
        await admin.ConfirmCod(paymentId, true, "Courier remitted the full delivery cash");
        Assert.Equal("Pending", (await db.Payments.AsNoTracking().SingleAsync()).Status);

        var order = await db.Orders.Include(x => x.Shipment).SingleAsync(x => x.Id == id);
        order.Status = "Delivered";
        order.Shipment!.Status = "Delivered";
        await db.SaveChangesAsync();
        await admin.ConfirmCod(paymentId, false, "Courier remitted the full delivery cash");
        Assert.Equal("Pending", (await db.Payments.AsNoTracking().SingleAsync()).Status);

        await admin.ConfirmCod(paymentId, true, "Courier remitted the full delivery cash");
        Assert.Equal("Paid", (await db.Payments.AsNoTracking().SingleAsync()).Status);
        Assert.NotNull((await db.Orders.AsNoTracking().SingleAsync()).PaidAt);
        Assert.Single(await db.PaymentTransactions.AsNoTracking().Where(x => x.TransactionType == "CashOnDelivery").ToListAsync());
        await admin.ConfirmCod(paymentId, true, "Courier remitted the full delivery cash");
        Assert.Single(await db.PaymentTransactions.AsNoTracking().ToListAsync());
    }

    private static DateTime BangladeshNow() => DateTime.UtcNow.AddHours(6);
    private static CheckoutService Checkout(ApplicationDbContext db) =>
        new(db, Options.Create(new CheckoutOptions()), new HttpContextAccessor(), new InventoryStockService(db));
    private static DeliveryInput Delivery() => new() { FullName = "Test Buyer",
        Email = "buyer@example.test", Phone = "01700000000", Region = "Dhaka", District = "Dhaka",
        City = "Dhaka", AddressLine1 = "Road 1" };
    private static ApplicationDbContext Database(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static void SeedCatalog(ApplicationDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "buyer", UserName = "buyer@example.test" },
            new ApplicationUser { Id = "other-buyer", UserName = "other@example.test" },
            new ApplicationUser { Id = "staff-1", UserName = "staff1@example.test" },
            new ApplicationUser { Id = "staff-2", UserName = "staff2@example.test" });
        db.Categories.Add(new Category { Id = 1, Name = "Jerseys", Slug = "jerseys" });
        db.Sizes.Add(new Size { Id = 1, Name = "M" });
        db.Colors.Add(new Color { Id = 1, Name = "Black" });
        db.Products.Add(new Product { Id = 1, Name = "Home Shirt", Slug = "home-shirt",
            CategoryId = 1, BasePrice = 1500m, IsPublished = true });
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1,
            SizeId = 1, ColorId = 1, SKU = "HOME-M", StockQuantity = 6,
            Price = 1500m, IsActive = true });
        db.ShoppingCarts.Add(new ShoppingCart { Id = 1, UserId = "buyer" });
        db.CartItems.Add(new CartItem { ShoppingCartId = 1, ProductVariantId = 1, Quantity = 2 });
    }

    private static T Setup<T>(T controller, string userId) where T : Controller
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, new TestTempDataProvider());
        return controller;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
