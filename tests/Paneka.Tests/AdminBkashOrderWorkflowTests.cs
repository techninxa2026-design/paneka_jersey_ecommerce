using System.Security.Claims;
using System.Text.Json;
using Jersey_ecommerce.Areas.Admin.Controllers;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Controllers;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Paneka.Tests;

public sealed class AdminBkashOrderWorkflowTests
{
    [Fact]
    public async Task Admin_order_page_verifies_bkash_and_unblocks_the_full_customer_shipment_flow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedManualOrder(db);
        await db.SaveChangesAsync();

        var orderAdmin = Orders(db);
        var initial = Assert.IsType<OrderDetailsViewModel>(
            Assert.IsType<ViewResult>(await orderAdmin.Details(1)).Model);
        Assert.Null(initial.NextStatus);
        Assert.Contains("Verify", initial.NextStatusHint ?? "");
        Assert.Equal("Submitted", initial.Order.Payment?.Status);
        Assert.Equal("Pending", initial.Order.Status);

        // The screenshot showed Packed while payment was unverified. Prevent
        // new packing and dispatch for unpaid manual bKash orders.
        var beforeVerification = Shipments(db);
        var packing = await beforeVerification.Edit(1, ShipmentEdit("Pending", "Packed"));
        Assert.IsType<ViewResult>(packing);
        Assert.False(beforeVerification.ModelState.IsValid);
        Assert.Equal("Pending", (await db.Shipments.AsNoTracking().SingleAsync()).Status);

        // Confirmation without checking the real wallet is not valid.
        await orderAdmin.VerifyBkash(1, "Matched transaction reference", independentlyVerified: false);
        Assert.Equal("Submitted", (await db.Payments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("Pending", (await db.Orders.AsNoTracking().SingleAsync()).Status);

        // Correct link from Order Details uses exactly the same payment service
        // and database transaction as the central verification queue.
        var result = await orderAdmin.VerifyBkash(1,
            "Verified merchant wallet, 14,000 BDT and exact TrxID from sender",
            independentlyVerified: true);
        Assert.IsType<RedirectToActionResult>(result);

        var payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Paid", payment.Status);
        Assert.NotNull(payment.PaidAt);
        Assert.Equal("admin", payment.ManualReviewedByUserId);
        Assert.NotNull(payment.ManualReviewedAt);
        Assert.Contains("14,000 BDT", payment.ManualReviewNote!);
        var order = await db.Orders.AsNoTracking().SingleAsync();
        Assert.Equal("Confirmed", order.Status);
        Assert.NotNull(order.PaidAt);
        Assert.Contains(await db.OrderStatusHistories.AsNoTracking().ToListAsync(),
            entry => entry.Status == "Confirmed" && entry.ChangedByUserId == "admin");
        Assert.Single(await db.PaymentTransactions.AsNoTracking().Where(t =>
            t.TransactionType == "ManualBkash" && t.Status == "Verified").ToListAsync());

        // The review queue is cleaned automatically after verification.
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Payment>>(
            Assert.IsType<ViewResult>(await ReviewQueue(db).Index()).Model));

        // A repeat button click cannot produce duplicate payments or histories.
        await Orders(db).VerifyBkash(1,
            "Verifying a payment twice must not create another record", true);
        Assert.Single(await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.Status == "Verified").ToListAsync());

        var ready = Assert.IsType<OrderDetailsViewModel>(
            Assert.IsType<ViewResult>(await Orders(db).Details(1)).Model);
        Assert.Equal("Processing", ready.NextStatus);

        var customer = Customer(db, "buyer");
        var customerOrder = Assert.IsType<Order>(Assert.IsType<ViewResult>(
            await customer.Details(1)).Model);
        Assert.Equal("Confirmed", customerOrder.Status);
        Assert.Equal("Paid", customerOrder.Payment?.Status);

        var customerPayment = Bkash(db, "buyer");
        var customerReceipt = Assert.IsType<ManualBkashStatusModel>(
            Assert.IsType<ViewResult>(await customerPayment.Status(1)).Model);
        Assert.Equal("Paid", customerReceipt.PaymentStatus);
        Assert.Equal("Confirmed", customerReceipt.OrderStatus);
        Assert.False(customerReceipt.CanSubmit);

        var liveStatus = Assert.IsType<OkObjectResult>(await customerPayment.State(1));
        using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(liveStatus.Value)))
        {
            Assert.Equal("Confirmed", snapshot.RootElement.GetProperty("orderStatus").GetString());
            Assert.Equal("Paid", snapshot.RootElement.GetProperty("paymentStatus").GetString());
        }
        Assert.IsType<NotFoundResult>(await Bkash(db, "other-buyer").State(1));

        await AdvanceOrder(db, "Confirmed", "Processing");
        await AdvanceShipment(db, "Pending", "Packed");
        await AdvanceShipment(db, "Packed", "Shipped");
        await AdvanceShipment(db, "Shipped", "InTransit");
        await AdvanceShipment(db, "InTransit", "OutForDelivery");
        await AdvanceShipment(db, "OutForDelivery", "Delivered");

        var delivered = await db.Orders.AsNoTracking()
            .Include(x => x.Shipment).Include(x => x.Payment).SingleAsync();
        Assert.Equal("Delivered", delivered.Status);
        Assert.Equal("Delivered", delivered.Shipment?.Status);
        Assert.Equal("Paid", delivered.Payment?.Status);
        Assert.NotNull(delivered.Shipment?.DeliveredAt);
        Assert.Contains(await db.OrderStatusHistories.AsNoTracking().ToListAsync(),
            h => h.Status == "Delivered");
        Assert.Equal("Delivered", Assert.IsType<Order>(
            Assert.IsType<ViewResult>(await customer.Details(1)).Model).Status);
    }

    [Fact]
    public async Task Admin_rejection_preserves_the_pending_order_and_customer_can_resubmit_and_be_confirmed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedManualOrder(db);
        await db.SaveChangesAsync();

        await Orders(db).RejectBkash(1, "No merchant-wallet transfer matches the customer's TrxID.");
        var payment = await db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("Rejected", payment.Status);
        Assert.Null(payment.PaidAt);
        Assert.Equal("Pending", (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Contains("No merchant-wallet", payment.ManualReviewNote!);
        Assert.Single(await db.PaymentTransactions.AsNoTracking()
            .Where(x => x.Status == "Rejected").ToListAsync());

        var customerReceipt = Assert.IsType<ManualBkashStatusModel>(
            Assert.IsType<ViewResult>(await Bkash(db, "buyer").Status(1)).Model);
        Assert.True(customerReceipt.CanSubmit);
        Assert.Equal("Rejected", customerReceipt.PaymentStatus);
        Assert.IsType<NotFoundResult>(await Bkash(db, "other-buyer").Status(1));

        await new ManualBkashPaymentService(db).SubmitAsync(
            1, "buyer", "01770000001", "VERIFIED12345", DateTime.UtcNow.AddHours(6));
        Assert.Equal("Submitted", (await db.Payments.AsNoTracking().SingleAsync()).Status);

        await Orders(db).VerifyBkash(1,
            "Matched corrected TrxID and exact order total in merchant wallet", true);
        Assert.Equal("Paid", (await db.Payments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("Confirmed", (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, await db.OrderStatusHistories.AsNoTracking()
            .CountAsync(x => x.Status == "Confirmed"));
    }

    private static ApplicationDbContext Database(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static OrdersController Orders(ApplicationDbContext db) =>
        Setup(new OrdersController(db, NullLogger<OrdersController>.Instance), "admin");
    private static ShipmentsController Shipments(ApplicationDbContext db) =>
        Setup(new ShipmentsController(db, NullLogger<ShipmentsController>.Instance), "admin");
    private static CheckoutPaymentReviewController ReviewQueue(ApplicationDbContext db) =>
        Setup(new CheckoutPaymentReviewController(db, new ManualBkashPaymentService(db)), "admin");
    private static MyOrdersController Customer(ApplicationDbContext db, string userId) =>
        Setup(new MyOrdersController(db,
            new OrderCancellationService(db, new InventoryStockService(db))), userId);
    private static BkashController Bkash(ApplicationDbContext db, string userId) =>
        Setup(new BkashController(db, new ManualBkashPaymentService(db),
            NullLogger<BkashController>.Instance), userId);

    private static T Setup<T>(T controller, string userId) where T : Controller
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userId)
        }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new StubTempDataProvider());
        return controller;
    }

    private static async Task AdvanceOrder(ApplicationDbContext db, string from, string to)
    {
        var controller = Orders(db);
        var action = await controller.ChangeStatus(1, new OrderStatusUpdateViewModel
        {
            OrderId = 1, ExpectedStatus = from, NewStatus = to
        });
        Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal(to, (await db.Orders.AsNoTracking().SingleAsync()).Status);
    }

    private static ShipmentFormViewModel ShipmentEdit(string oldStatus, string status) =>
        new()
        {
            Id = 1, OrderId = 1, ExpectedStatus = oldStatus, Status = status,
            CourierName = "Pathao", TrackingNumber = "TRK-123456",
            TrackingUrl = "https://example.com/tracking/TRK-123456"
        };

    private static async Task AdvanceShipment(ApplicationDbContext db, string from, string to)
    {
        var ctrl = Shipments(db);
        Assert.IsType<RedirectToActionResult>(await ctrl.Edit(1, ShipmentEdit(from, to)));
        Assert.True(ctrl.ModelState.IsValid);
        Assert.Equal(to, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
    }

    private static void SeedManualOrder(ApplicationDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "admin", UserName = "admin@example.test" },
            new ApplicationUser { Id = "buyer", UserName = "buyer@example.test" },
            new ApplicationUser { Id = "other-buyer", UserName = "other@example.test" });
        db.Orders.Add(new Order
        {
            Id = 1, UserId = "buyer", OrderNumber = "PNK-SAMPLE-14000",
            Status = "Pending", CustomerName = "Customer", CustomerEmail = "buyer@example.test",
            CustomerPhone = "01770000001", ShippingAddressLine1 = "Village road",
            ShippingCity = "Kaliganj", ShippingDistrict = "Gazipur",
            ShippingCountry = "Bangladesh", Subtotal = 14000m, TotalAmount = 14000m
        });
        db.Payments.Add(new Payment
        {
            Id = 1, OrderId = 1, Method = "Bkash", Status = "Submitted",
            Provider = "bKash (manual transfer)", Amount = 14000m,
            ManualSenderNumber = "01770000001", ManualRecipientNumber = null,
            TransactionReference = "ORIGINAL1234",
            ManualClaimedPaidAt = DateTime.UtcNow.AddMinutes(-4),
            ManualSubmittedAt = DateTime.UtcNow.AddMinutes(-3)
        });
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            PaymentId = 1, Status = "Submitted", TransactionType = "ManualBkash",
            Reference = "01770000001", ProviderTransactionId = "ORIGINAL1234",
            Amount = 14000m, CreatedAt = DateTime.UtcNow.AddMinutes(-3)
        });
        db.Shipments.Add(new Shipment { Id = 1, OrderId = 1, Status = "Pending" });
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = 1, Status = "Pending", CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Note = "Order created from the PANENKA storefront."
        });
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
