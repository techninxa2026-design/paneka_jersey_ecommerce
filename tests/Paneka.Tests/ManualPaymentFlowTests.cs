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
    public void Official_bangladesh_district_selector_has_all_64_unique_names_and_canonical_aliases()
    {
        var districts = BangladeshDistricts.ByDivision
            .SelectMany(d => d.Districts).ToList();
        Assert.Equal(8, BangladeshDistricts.ByDivision.Count);
        Assert.Equal(64, districts.Count);
        Assert.Equal(64, districts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Dhaka", districts);
        Assert.Contains("Gazipur", districts);
        Assert.Contains("Jashore", districts);
        Assert.Equal("Chattogram", BangladeshDistricts.Normalize("Chittagong"));
        Assert.Equal("Cumilla", BangladeshDistricts.Normalize("Comilla"));
        Assert.False(BangladeshDistricts.IsValid("Kolkata"));
        Assert.False(BangladeshDistricts.IsValid("Kaliganj"));
    }

    [Fact]
    public async Task Abandoned_bkash_reservation_requires_staff_wallet_reconciliation_before_stock_release()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        db.SiteSettings.Add(new SiteSettings
        {
            Key = ManualBkashPaymentService.NumberSettingKey, Value = "01712345678"
        });
        await db.SaveChangesAsync();

        var reserve = BkashInput("AB33221177");
        reserve.BkashSenderNumber = null;
        reserve.BkashTransactionId = null;
        reserve.BkashPaidAtLocal = null;
        var orderId = await Checkout(db).PlaceOrder("buyer", Delivery(), reserve,
            prepareManualBkash: true);
        Assert.Equal(4, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);

        var cancellation = new OrderCancellationService(db, new InventoryStockService(db));
        await Assert.ThrowsAsync<CheckoutException>(() =>
            cancellation.CancelUnpaidPendingOrder(orderId, "buyer"));
        await Assert.ThrowsAsync<CheckoutException>(() =>
            cancellation.CancelUnpaidPendingOrder(orderId, "staff-1", asAdmin: true));
        Assert.Equal(4, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);

        // If the customer reported a transfer, staff must first reject it
        // after checking the actual wallet. No direct stock release is allowed.
        var manual = new ManualBkashPaymentService(db);
        await manual.SubmitAsync(orderId, "buyer", "01912345678", "AB33221177",
            BangladeshNow());
        await Assert.ThrowsAsync<CheckoutException>(() =>
            cancellation.CancelUnpaidPendingOrder(orderId, "staff-1", asAdmin: true,
                financeNote: "No matching funds received in the merchant wallet."));
        var paymentId = (await db.Payments.AsNoTracking().SingleAsync()).Id;
        await manual.RejectAsync(paymentId, "staff-1",
            "No matching funds in the merchant wallet.");
        await cancellation.CancelUnpaidPendingOrder(orderId, "staff-1",
            asAdmin: true, financeNote: "Checked wallet and confirmed no funds reached merchant account.");

        var cancelled = await db.Orders.AsNoTracking()
            .Include(x => x.Payment).Include(x => x.Shipment).SingleAsync();
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Cancelled", cancelled.Payment?.Status);
        Assert.Equal("Cancelled", cancelled.Shipment?.Status);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.Equal(6, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);
        Assert.Single(await db.InventoryTransactions.AsNoTracking()
            .Where(x => x.Type == "Restock").ToListAsync());
        Assert.Contains(await db.PaymentTransactions.AsNoTracking().ToListAsync(),
            x => x.Status == "Cancelled" && x.TransactionType == "ManualBkash");
        Assert.Equal("staff-1", cancelled.Payment?.ManualReviewedByUserId);
        await Assert.ThrowsAsync<CheckoutException>(() =>
            manual.SubmitAsync(orderId, "buyer", "01912345678", "ZX99887766",
                BangladeshNow()));
    }

    [Fact]
    public async Task Bkash_popup_reserves_order_before_transfer_and_then_links_receipt_to_same_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        db.SiteSettings.Add(new SiteSettings
        {
            Key = ManualBkashPaymentService.NumberSettingKey, Value = "01712345678"
        });
        await db.SaveChangesAsync();

        var checkout = Checkout(db);
        var token = Guid.NewGuid().ToString("N");
        var prepare = new PlaceOrderInput
        {
            PaymentMethod = "Bkash",
            BkashRecipientNumber = "01712345678",
            SubmissionToken = token
        };
        var id = await checkout.PlaceOrder("buyer", Delivery(), prepare,
            prepareManualBkash: true);

        var order = await db.Orders.AsNoTracking().Include(x => x.Payment)
            .Include(x => x.Shipment).SingleAsync(x => x.Id == id);
        Assert.Equal("Pending", order.Status);
        Assert.Equal("Pending", order.Payment?.Status);
        Assert.Equal("Pending", order.Shipment?.Status);
        Assert.Equal("01712345678", order.Payment?.ManualRecipientNumber);
        Assert.Null(order.Payment?.TransactionReference);
        Assert.Empty(await db.PaymentTransactions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.CartItems.AsNoTracking().ToListAsync());
        Assert.Equal(4, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);

        // Double-click and network retry must never reserve a second order.
        var repeated = await checkout.PlaceOrder("buyer", Delivery(), prepare,
            prepareManualBkash: true);
        Assert.Equal(id, repeated);
        Assert.Single(await db.Orders.AsNoTracking().ToListAsync());
        Assert.Equal(4, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);

        // Customer finishes sending money INSIDE the checkout modal.
        var manual = new ManualBkashPaymentService(db);
        await manual.SubmitAsync(id, "buyer", "01912345678", "PX12345678",
            BangladeshNow());
        var submitted = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Submitted", submitted.Status);
        Assert.Equal("PX12345678", submitted.TransactionReference);
        Assert.Equal("01912345678", submitted.ManualSenderNumber);
        Assert.NotNull(submitted.ManualSubmittedAt);
        Assert.Null(submitted.PaidAt);
        Assert.Single(await db.PaymentTransactions.AsNoTracking().ToListAsync());

        await manual.VerifyAsync(submitted.Id, "staff-1",
            "Verified matching TrxID and exact amount in merchant wallet");
        Assert.Equal("Paid", (await db.Payments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("Confirmed", (await db.Orders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Checkout_never_creates_an_order_for_a_fake_district_or_unreported_bkash_transfer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        db.SiteSettings.Add(new SiteSettings
        {
            Key = ManualBkashPaymentService.NumberSettingKey, Value = "01712345678"
        });
        await db.SaveChangesAsync();
        var checkout = Checkout(db);
        var badAddress = Delivery();
        badAddress.District = "Kolkata";
        badAddress.Region = "OutsideDhaka";
        await Assert.ThrowsAsync<CheckoutException>(() =>
            checkout.PlaceOrder("buyer", badAddress, BkashInput("AD98765432")));
        var genuineArea = Delivery();
        genuineArea.District = "Gazipur";
        genuineArea.City = "Kaliganj";
        genuineArea.Region = "Dhaka";
        await Assert.ThrowsAsync<CheckoutException>(() =>
            checkout.PlaceOrder("buyer", genuineArea, BkashInput("AD98765432")));
        genuineArea.Region = "OutsideDhaka";

        var missingSender = BkashInput("AD98765432");
        missingSender.BkashSenderNumber = "";
        await Assert.ThrowsAsync<CheckoutException>(() =>
            checkout.PlaceOrder("buyer", genuineArea, missingSender));
        var missingTrx = BkashInput("AD98765432");
        missingTrx.BkashTransactionId = null;
        await Assert.ThrowsAsync<CheckoutException>(() =>
            checkout.PlaceOrder("buyer", genuineArea, missingTrx));
        var wrongRecipient = BkashInput("AD98765432");
        wrongRecipient.BkashRecipientNumber = "01811111111";
        await Assert.ThrowsAsync<CheckoutException>(() =>
            checkout.PlaceOrder("buyer", genuineArea, wrongRecipient));
        var futureTime = BkashInput("AD98765432");
        futureTime.BkashPaidAtLocal = BangladeshNow().AddDays(2);
        await Assert.ThrowsAsync<CheckoutException>(() =>
            checkout.PlaceOrder("buyer", genuineArea, futureTime));

        Assert.Empty(await db.Orders.AsNoTracking().ToListAsync());
        Assert.Equal(6, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);
        Assert.Single(await db.CartItems.AsNoTracking().ToListAsync());

        var id = await checkout.PlaceOrder("buyer", genuineArea, BkashInput("AD98765432"));
        var order = await db.Orders.AsNoTracking().Include(x => x.Payment).SingleAsync(x => x.Id == id);
        Assert.Equal("Gazipur", order.ShippingDistrict);
        Assert.Equal("Kaliganj", order.ShippingCity);
        Assert.Equal("Submitted", order.Payment?.Status);
        Assert.Equal("AD98765432", order.Payment?.TransactionReference);
        Assert.Equal(4, (await db.ProductVariants.AsNoTracking().SingleAsync()).StockQuantity);
        Assert.Empty(await db.CartItems.AsNoTracking().ToListAsync());
        Assert.Single(await db.PaymentTransactions.AsNoTracking().ToListAsync());
    }

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
        var orderId = await checkout.PlaceOrder("buyer", Delivery(), BkashInput("BE12345678"));

        var payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Bkash", payment.Method);
        Assert.Equal("Submitted", payment.Status);
        Assert.Equal("01712345678", payment.ManualRecipientNumber);
        Assert.Equal("BE12345678", payment.TransactionReference);
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
        var orderId = await Checkout(db).PlaceOrder("buyer", Delivery(), BkashInput("BE00000001"));
        var paymentId = (await db.Payments.AsNoTracking().SingleAsync()).Id;
        var service = new ManualBkashPaymentService(db);
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
    public async Task Missing_merchant_number_keeps_bkash_active_and_accepts_sender_and_trx()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedCatalog(db);
        await db.SaveChangesAsync();
        var checkout = Checkout(db);
        Assert.Null(await checkout.ManualBkashNumber());
        Assert.True(new PaymentPageViewModel().AllowManualBkash);

        // No merchant number in Admin: buyer can still use the manual bKash
        // popup and report a transfer made using store-provided details.
        var reservation = new PlaceOrderInput
        {
            PaymentMethod = "Bkash",
            SubmissionToken = Guid.NewGuid().ToString("N")
        };
        var id = await checkout.PlaceOrder("buyer", Delivery(), reservation,
            prepareManualBkash: true);
        var payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Bkash", payment.Method);
        Assert.Equal("Pending", payment.Status);
        Assert.Null(payment.ManualRecipientNumber);
        Assert.Null(payment.PaidAt);

        var service = new ManualBkashPaymentService(db);
        var customer = Setup(new BkashController(db, service,
            NullLogger<BkashController>.Instance), "buyer");
        var receipt = Assert.IsType<ManualBkashStatusModel>(
            Assert.IsType<ViewResult>(await customer.Status(id)).Model);
        Assert.True(receipt.CanSubmit);
        Assert.Null(receipt.RecipientNumber);

        await service.SubmitAsync(id, "buyer", "01912345678", "PX123456789",
            BangladeshNow());
        payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Submitted", payment.Status);
        Assert.Equal("01912345678", payment.ManualSenderNumber);
        Assert.Equal("PX123456789", payment.TransactionReference);
        Assert.Null(payment.ManualRecipientNumber);
        Assert.NotNull(payment.ManualSubmittedAt);
        Assert.Null(payment.PaidAt);
        Assert.Equal("Pending", (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await db.PaymentTransactions.AsNoTracking().ToListAsync());

        await service.VerifyAsync(payment.Id, "staff-1",
            "Confirmed full transfer amount and TrxID in the actual receiving wallet");
        Assert.Equal("Paid", (await db.Payments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("Confirmed", (await db.Orders.AsNoTracking().SingleAsync()).Status);

        // Cash on delivery is still separately available for new orders.
        var cart = await db.ShoppingCarts.SingleAsync();
        db.CartItems.Add(new CartItem
        {
            ShoppingCartId = cart.Id, ProductVariantId = 1, Quantity = 1
        });
        await db.SaveChangesAsync();
        var cod = await checkout.PlaceOrder("buyer", Delivery(), new PlaceOrderInput
        {
            PaymentMethod = "COD", SubmissionToken = Guid.NewGuid().ToString("N")
        });
        Assert.True(cod > 0);
        Assert.Equal("CashOnDelivery",
            (await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == cod)).Method);
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

        var orderId = await Checkout(db).PlaceOrder("buyer", Delivery(), BkashInput("AB12345678"));
        var service = new ManualBkashPaymentService(db);

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

    private static PlaceOrderInput BkashInput(string trxId) => new()
    {
        PaymentMethod = "Bkash", SubmissionToken = Guid.NewGuid().ToString("N"),
        BkashRecipientNumber = "01712345678",
        BkashSenderNumber = "01912345678",
        BkashTransactionId = trxId,
        BkashPaidAtLocal = BangladeshNow()
    };

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
