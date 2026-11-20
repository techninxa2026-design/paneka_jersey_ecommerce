using System.Security.Claims;
using Jersey_ecommerce.Areas.Admin.Controllers;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Customers;
using Jersey_ecommerce.Models.Notifications;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Color = Jersey_ecommerce.Models.Catalog.Color;

namespace Paneka.Tests;

public sealed class AdminNotificationsTests
{
    [Fact]
    public async Task Order_payment_shipment_and_review_events_produce_persistent_targeted_notifications()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await using var db = Database(conn);
        await db.Database.EnsureCreatedAsync();

        // Accounts are monitored by the same persistence boundary.
        db.Users.AddRange(
            new ApplicationUser { Id = "buyer", UserName = "buyer@example.test", Email = "buyer@example.test" },
            new ApplicationUser { Id = "admin1", UserName = "admin@example.test", Email = "admin@example.test" });
        await db.SaveChangesAsync();
        Assert.Contains(await db.AdminNotifications.ToListAsync(), n => n.EventType == "customer.registered");

        // Other system writes should not synthesize duplicate events.
        var initial = await db.AdminNotifications.CountAsync();
        await db.SaveChangesAsync();
        Assert.Equal(initial, await db.AdminNotifications.CountAsync());

        db.Categories.Add(new Category { Id = 1, Name = "Jersey", Slug = "jersey" });
        db.Products.Add(new Product { Id = 1, CategoryId = 1, Name = "Jersey", Slug = "jersey",
            BasePrice = 1200m, IsPublished = true });
        db.Sizes.Add(new Size { Id = 1, Name = "M" });
        db.Colors.Add(new Color { Id = 1, Name = "Black" });
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1, SizeId = 1,
            ColorId = 1, SKU = "JR-M", StockQuantity = 6, Price = 1200m });
        await db.SaveChangesAsync();

        db.Orders.Add(new Order
        {
            Id = 1, UserId = "buyer", OrderNumber = "PNK-EVENT-1", Status = "Pending",
            CustomerName = "Buyer", CustomerEmail = "buyer@example.test",
            CustomerPhone = "01712345678", ShippingAddressLine1 = "Test road",
            ShippingCity = "Dhaka", ShippingCountry = "Bangladesh", TotalAmount = 1200m
        });
        db.Payments.Add(new Payment { Id = 1, OrderId = 1, Method = "Bkash",
            Status = "Pending", Amount = 1200m });
        db.Shipments.Add(new Shipment { Id = 1, OrderId = 1, Status = "Pending" });
        await db.SaveChangesAsync();

        var orderAlert = await db.AdminNotifications.SingleAsync(x => x.EventType == "order.created");
        Assert.Equal("/Admin/Orders/Details/1", orderAlert.TargetUrl);

        var payment = await db.Payments.SingleAsync();
        payment.ManualSenderNumber = "01712345678";
        payment.ManualSubmittedAt = DateTime.UtcNow;
        payment.TransactionReference = "ABC1234567";
        payment.Status = "Submitted";
        await db.SaveChangesAsync();
        var claim = await db.AdminNotifications.SingleAsync(x => x.EventType == "payment.submitted");
        Assert.Equal("Warning", claim.Severity);
        Assert.Contains("/Admin/Orders/Details/1", claim.TargetUrl);

        payment.Status = "Paid";
        var order = await db.Orders.SingleAsync();
        order.Status = "Confirmed";
        await db.SaveChangesAsync();
        Assert.Single(await db.AdminNotifications.Where(x => x.EventType == "payment.paid").ToListAsync());
        Assert.Empty(await db.AdminNotifications.Where(x =>
            x.EventType == "order.status" && x.Title.Contains("Confirmed")).ToListAsync());

        var shipment = await db.Shipments.SingleAsync();
        shipment.Status = "Packed";
        await db.SaveChangesAsync();
        Assert.Single(await db.AdminNotifications.Where(x => x.EventType == "shipment.packed").ToListAsync());

        db.Reviews.Add(new Review { Id = 1, ProductId = 1, UserId = "buyer",
            Rating = 5, Comment = "Good quality" });
        await db.SaveChangesAsync();
        var moderation = await db.AdminNotifications.SingleAsync(x => x.EventType == "review.submitted");
        Assert.Equal("/Admin/Reviews/Details/1", moderation.TargetUrl);

        var variant = await db.ProductVariants.SingleAsync();
        variant.StockQuantity = 5;
        await db.SaveChangesAsync();
        Assert.Single(await db.AdminNotifications.Where(x => x.EventType == "stock.low").ToListAsync());
        variant.StockQuantity = 4;
        await db.SaveChangesAsync();
        Assert.Single(await db.AdminNotifications.Where(x => x.EventType == "stock.low").ToListAsync());
        variant.StockQuantity = 0;
        await db.SaveChangesAsync();
        Assert.Single(await db.AdminNotifications.Where(x => x.EventType == "stock.out").ToListAsync());
    }

    [Fact]
    public async Task Read_receipts_are_per_admin_and_reading_never_generates_an_event()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await using var db = Database(conn);
        await db.Database.EnsureCreatedAsync();
        db.AdminNotifications.Add(new AdminNotification
        {
            Category = "Orders", EventType = "order.created", Severity = "Info",
            Title = "New order", Message = "Order received.",
            TargetUrl = "/Admin/Orders/Details/42"
        });
        await db.SaveChangesAsync();
        var id = (await db.AdminNotifications.SingleAsync()).Id;
        var reader = new AdminNotificationReader(db);
        Assert.Equal(1, await reader.UnreadCountAsync("admin-1"));
        Assert.Equal(1, await reader.UnreadCountAsync("admin-2"));

        var controller = Setup(new NotificationsController(db, reader), "admin-1");
        var opened = Assert.IsType<LocalRedirectResult>(await controller.Open(id));
        Assert.Equal("/Admin/Orders/Details/42", opened.Url);
        Assert.Equal(0, await reader.UnreadCountAsync("admin-1"));
        Assert.Equal(1, await reader.UnreadCountAsync("admin-2"));
        Assert.Single(await db.AdminNotifications.ToListAsync());

        await controller.Open(id);
        Assert.Single(await db.AdminNotificationReads.ToListAsync());

        await Setup(new NotificationsController(db, reader), "admin-2").ReadAll();
        Assert.Equal(0, await reader.UnreadCountAsync("admin-2"));
        await controller.Unread(id);
        Assert.Equal(1, await reader.UnreadCountAsync("admin-1"));
        Assert.Equal(0, await reader.UnreadCountAsync("admin-2"));
        Assert.Single(await db.AdminNotifications.ToListAsync());

        var attribute = Assert.Single(typeof(NotificationsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", attribute.Roles);
    }

    [Fact]
    public async Task Failed_business_transaction_does_not_leave_phantom_notifications()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await using var db = Database(conn);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "buyer", UserName = "buyer" });
        await db.SaveChangesAsync();
        var initial = await db.AdminNotifications.CountAsync();

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.Orders.Add(new Order
            {
                UserId = "buyer", OrderNumber = "PNK-ROLLBACK", Status = "Pending",
                CustomerName = "Buyer", CustomerEmail = "buyer@example.test",
                CustomerPhone = "01711111111", ShippingAddressLine1 = "Road",
                ShippingCity = "Dhaka", ShippingCountry = "Bangladesh"
            });
            await db.SaveChangesAsync();
            Assert.Equal(initial + 1, await db.AdminNotifications.CountAsync());
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Equal(initial, await db.AdminNotifications.CountAsync());
        Assert.Empty(await db.Orders.ToListAsync());
    }

    private static ApplicationDbContext Database(SqliteConnection conn) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conn).Options);

    private static NotificationsController Setup(NotificationsController controller, string userId)
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, "Admin")
        }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new StubTempDataProvider());
        return controller;
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
