using System.Security.Claims;
using Jersey_ecommerce.Areas.Admin.Controllers;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Controllers;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Customers;
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
using Color = Jersey_ecommerce.Models.Catalog.Color;

namespace Paneka.Tests;

public sealed class CustomerFulfillmentTests
{
    [Theory]
    [InlineData("Pending", "Packed", true)]
    [InlineData("Pending", "Delivered", false)]
    [InlineData("Packed", "Shipped", true)]
    [InlineData("Shipped", "OutForDelivery", true)]
    [InlineData("OutForDelivery", "Delivered", true)]
    [InlineData("Delivered", "Pending", false)]
    [InlineData("Returned", "Shipped", false)]
    [InlineData("Cancelled", "Packed", false)]
    public void Courier_states_enforce_the_delivery_sequence(string oldStatus, string next, bool allowed) =>
        Assert.Equal(allowed, ShipmentStatuses.CanTransition(oldStatus, next));

    [Fact]
    public async Task Admin_to_customer_delivery_flow_is_consistent_and_private()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedOrder(db);
        await db.SaveChangesAsync();

        var bad = Shipments(db);
        var invalid = await bad.Edit(1, EditShipment("Pending", "Delivered"));
        Assert.IsType<ViewResult>(invalid);
        Assert.False(bad.ModelState.IsValid);
        Assert.Equal("Pending", (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("Pending", (await db.Orders.AsNoTracking().SingleAsync()).Status);

        var skip = Orders(db);
        var skipResult = await skip.ChangeStatus(1, new OrderStatusUpdateViewModel {
            OrderId = 1, ExpectedStatus = "Pending", NewStatus = "Shipped"
        });
        Assert.IsType<RedirectToActionResult>(skipResult);
        Assert.Equal("Pending", (await db.Orders.AsNoTracking().SingleAsync()).Status);

        await AdvanceOrder(db, "Pending", "Confirmed");
        await AdvanceOrder(db, "Confirmed", "Processing");
        var blockedPage = Assert.IsType<ViewResult>(await Orders(db).Details(1));
        var blockedModel = Assert.IsType<OrderDetailsViewModel>(blockedPage.Model);
        Assert.Null(blockedModel.NextStatus);
        Assert.Contains("Packed", blockedModel.NextStatusHint ?? "");

        await AdvanceShipment(db, "Pending", "Packed");
        var readyPage = Assert.IsType<ViewResult>(await Orders(db).Details(1));
        Assert.Equal("Shipped", Assert.IsType<OrderDetailsViewModel>(readyPage.Model).NextStatus);
        await AdvanceShipment(db, "Packed", "Shipped");
        Assert.Equal("Shipped", (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.NotNull((await db.Shipments.AsNoTracking().SingleAsync()).ShippedAt);

        await AdvanceShipment(db, "Shipped", "InTransit");
        await AdvanceShipment(db, "InTransit", "OutForDelivery");
        await AdvanceShipment(db, "OutForDelivery", "Delivered");

        var order = await db.Orders.AsNoTracking().Include(x => x.Shipment).SingleAsync();
        Assert.Equal("Delivered", order.Status);
        Assert.Equal("Delivered", order.Shipment?.Status);
        Assert.Equal("Steadfast", order.Shipment?.CourierName);
        Assert.Equal("PAN-123", order.Shipment?.TrackingNumber);
        Assert.Equal("https://courier.example/track/PAN-123", order.Shipment?.TrackingUrl);
        Assert.NotNull(order.Shipment?.DeliveredAt);
        Assert.Contains(await db.OrderStatusHistories.ToListAsync(), h => h.Status == "Delivered");

        var buyer = Customer(db, "buyer");
        var details = await buyer.Details(1);
        var page = Assert.IsType<ViewResult>(details);
        Assert.Equal("Delivered", Assert.IsType<Order>(page.Model).Shipment?.Status);

        var stranger = Customer(db, "another-buyer");
        Assert.IsType<NotFoundResult>(await stranger.Details(1));
        Assert.IsType<NotFoundResult>(await stranger.Details(9999));

        // Shipped stock cannot be returned by a customer cancellation shortcut.
        await Assert.ThrowsAsync<CheckoutException>(
            () => new OrderCancellationService(db, new InventoryStockService(db))
                .CancelUnpaidPendingOrder(1, "buyer"));
    }

    [Fact]
    public async Task Customer_review_only_appears_after_delivery_and_admin_approval()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        SeedOrder(db);
        await db.SaveChangesAsync();

        var buyer = ProductPage(db, "buyer");
        await buyer.Review(1, 5, "Fantastic", "Great quality and quick delivery");
        Assert.Empty(await db.Reviews.ToListAsync());

        var shippedOrder = await db.Orders.SingleAsync();
        shippedOrder.Status = "Delivered";
        await db.SaveChangesAsync();

        await ProductPage(db, "buyer").Review(1, 5, "Fantastic", "Great quality and quick delivery");
        var review = await db.Reviews.SingleAsync();
        Assert.False(review.IsApproved);

        // Pending reviews are not rendered to public shoppers.
        var publicPage = Assert.IsType<ViewResult>(await ProductPage(db, "another-buyer").Details("home-shirt"));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Review>>(publicPage.ViewData["Reviews"]));

        var admin = Reviews(db);
        await admin.Approve(review.Id, null, null, null);
        Assert.True((await db.Reviews.SingleAsync()).IsApproved);
        var approvedPage = Assert.IsType<ViewResult>(await ProductPage(db, "another-buyer").Details("home-shirt"));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Review>>(approvedPage.ViewData["Reviews"]));

        await ProductPage(db, "buyer").Review(1, 5, "Repeat", "This should not create another review.");
        Assert.Single(await db.Reviews.ToListAsync());
    }

    private static ShipmentFormViewModel EditShipment(string oldState, string newState) =>
        new() { Id = 1, OrderId = 1, ExpectedStatus = oldState, Status = newState,
            CourierName = "Steadfast", TrackingNumber = "PAN-123",
            TrackingUrl = "https://courier.example/track/PAN-123" };

    private static async Task AdvanceOrder(ApplicationDbContext db, string oldStatus, string newStatus)
    {
        var action = await Orders(db).ChangeStatus(1, new OrderStatusUpdateViewModel {
            OrderId = 1, ExpectedStatus = oldStatus, NewStatus = newStatus
        });
        Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal(newStatus, (await db.Orders.AsNoTracking().SingleAsync()).Status);
    }

    private static async Task AdvanceShipment(ApplicationDbContext db, string oldStatus, string newStatus)
    {
        var controller = Shipments(db);
        var action = await controller.Edit(1, EditShipment(oldStatus, newStatus));
        Assert.IsType<RedirectToActionResult>(action);
        Assert.True(controller.ModelState.IsValid, "Shipment status update was rejected.");
        Assert.Equal(newStatus, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
    }

    private static ShipmentsController Shipments(ApplicationDbContext db) =>
        Setup(new ShipmentsController(db, NullLogger<ShipmentsController>.Instance), "admin");

    private static OrdersController Orders(ApplicationDbContext db) =>
        Setup(new OrdersController(db, NullLogger<OrdersController>.Instance), "admin");

    private static ReviewsController Reviews(ApplicationDbContext db) =>
        Setup(new ReviewsController(db), "admin");

    private static ProductController ProductPage(ApplicationDbContext db, string user) =>
        Setup(new ProductController(db), user);

    private static MyOrdersController Customer(ApplicationDbContext db, string user) =>
        Setup(new MyOrdersController(db, new OrderCancellationService(db, new InventoryStockService(db))), user);

    private static T Setup<T>(T controller, string user) where T : Controller
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user) }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, new TestTempDataProvider());
        return controller;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static ApplicationDbContext Database(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static void SeedOrder(ApplicationDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "admin", UserName = "admin@example.test" },
            new ApplicationUser { Id = "buyer", UserName = "buyer@example.test" },
            new ApplicationUser { Id = "another-buyer", UserName = "other@example.test" });
        db.Categories.Add(new Category { Id = 1, Name = "Home Kits", Slug = "home-kits" });
        db.Sizes.Add(new Size { Id = 1, Name = "M", IsActive = true });
        db.Colors.Add(new Color { Id = 1, Name = "Black", IsActive = true });
        db.Products.Add(new Product { Id = 1, Name = "Home Shirt", Slug = "home-shirt",
            CategoryId = 1, BasePrice = 1600m, IsPublished = true });
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1,
            SizeId = 1, ColorId = 1, Price = 1600m, StockQuantity = 9,
            SKU = "PN-HOME-M", IsActive = true });
        db.Orders.Add(new Order { Id = 1, UserId = "buyer", OrderNumber = "PNK-TEST-123",
            Status = "Pending", CustomerName = "Test Buyer", CustomerEmail = "buyer@example.test",
            CustomerPhone = "01700000000", ShippingAddressLine1 = "Test Road",
            ShippingCity = "Dhaka", ShippingCountry = "Bangladesh",
            Subtotal = 1600m, TotalAmount = 1600m });
        db.OrderItems.Add(new OrderItem { Id = 1, OrderId = 1, ProductVariantId = 1,
            ProductName = "Home Shirt", SizeName = "M", ColorName = "Black",
            Quantity = 1, UnitPrice = 1600m, LineTotal = 1600m });
        db.Payments.Add(new Payment { OrderId = 1, Method = "CashOnDelivery",
            Status = "Pending", Amount = 1600m });
        db.Shipments.Add(new Shipment { Id = 1, OrderId = 1, Status = "Pending" });
        db.OrderStatusHistories.Add(new OrderStatusHistory {
            OrderId = 1, Status = "Pending", CreatedAt = DateTime.UtcNow });
    }
}
