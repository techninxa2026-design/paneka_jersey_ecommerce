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
using Jersey_ecommerce.ViewModels.Orders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Color = Jersey_ecommerce.Models.Catalog.Color;

namespace Paneka.Tests;

public sealed class DeliveredReviewWorkflowTests
{
    [Fact]
    public async Task Delivered_order_review_reaches_admin_moderation_then_public_product_and_order_state()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = NewDb(connection);
        await db.Database.EnsureCreatedAsync();
        Seed(db);
        await db.SaveChangesAsync();

        var buyer = Customer(db, "buyer");
        Assert.IsType<NotFoundResult>(await buyer.Review(1, 1));
        Assert.IsType<NotFoundResult>(await buyer.Review(1, 1, 5, "Nice", "Looks fantastic"));
        Assert.Empty(await db.Reviews.AsNoTracking().ToListAsync());
        Assert.IsType<NotFoundResult>(await Customer(db, "other").Review(1, 1));

        var order = await db.Orders.SingleAsync();
        order.Status = "Delivered";
        await db.SaveChangesAsync();

        Assert.IsType<NotFoundResult>(await Customer(db, "other").Review(1, 1));
        Assert.IsType<NotFoundResult>(await Customer(db, "buyer").Review(1, 999));
        var form = Assert.IsType<DeliveredProductReviewViewModel>(
            Assert.IsType<ViewResult>(await Customer(db, "buyer").Review(1, 1)).Model);
        Assert.True(form.CanSubmit);
        Assert.Equal("NotReviewed", form.ReviewStatus);
        Assert.Equal("Home Shirt", form.ProductName);

        var invalid = await Customer(db, "buyer").Review(1, 1, 9, "Rating", "Good quality and comfortable");
        Assert.IsType<RedirectToActionResult>(invalid);
        Assert.Empty(await db.Reviews.AsNoTracking().ToListAsync());

        var result = await Customer(db, "buyer").Review(
            1, 1, 5, "Perfect for matchdays", "The product looks excellent and fits perfectly.");
        Assert.IsType<RedirectToActionResult>(result);

        var review = await db.Reviews.SingleAsync();
        Assert.False(review.IsApproved);
        Assert.False(review.IsRejected);
        Assert.Equal(5, review.Rating);
        Assert.Equal("Perfect for matchdays", review.Title);
        Assert.Single(await db.AdminNotifications.AsNoTracking()
            .Where(x => x.EventType == "review.submitted").ToListAsync());

        var pending = Assert.IsType<DeliveredProductReviewViewModel>(
            Assert.IsType<ViewResult>(await Customer(db, "buyer").Review(1, 1)).Model);
        Assert.Equal("Pending", pending.ReviewStatus);
        Assert.False(pending.CanSubmit);
        var orderPage = Assert.IsType<Order>(
            Assert.IsType<ViewResult>(await Customer(db, "buyer").Details(1)).Model);
        Assert.Equal("Delivered", orderPage.Status);

        var adminPage = Assert.IsType<ReviewsIndexViewModel>(
            Assert.IsType<ViewResult>(await Reviews(db).Index(null)).Model);
        Assert.Equal(1, adminPage.PendingReviews);
        Assert.Equal(1, adminPage.TotalReviews);
        Assert.Equal(review.Id, Assert.Single(adminPage.Reviews).Id);

        var beforeApproval = Assert.IsType<ViewResult>(
            await Product(db, "other").Details("home-shirt"));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Review>>(
            beforeApproval.ViewData["Reviews"]));

        await Reviews(db).Approve(review.Id, null, null, null);
        Assert.True((await db.Reviews.SingleAsync()).IsApproved);
        Assert.Equal(1, await db.AdminNotifications.CountAsync(x => x.EventType == "review.approved"));

        var approved = Assert.IsType<DeliveredProductReviewViewModel>(
            Assert.IsType<ViewResult>(await Customer(db, "buyer").Review(1, 1)).Model);
        Assert.Equal("Approved", approved.ReviewStatus);
        Assert.False(approved.CanSubmit);

        var afterApproval = Assert.IsType<ViewResult>(
            await Product(db, "other").Details("home-shirt"));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Review>>(
            afterApproval.ViewData["Reviews"]));

        await Customer(db, "buyer").Review(
            1, 1, 4, "Changed mind", "This should not overwrite a published review.");
        Assert.Single(await db.Reviews.AsNoTracking().ToListAsync());
        Assert.Equal(5, (await db.Reviews.AsNoTracking().SingleAsync()).Rating);
    }

    [Fact]
    public async Task Rejected_review_can_be_revised_and_resubmitted_once_without_losing_moderation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = NewDb(connection);
        await db.Database.EnsureCreatedAsync();
        Seed(db);
        (await db.Orders.SingleAsync()).Status = "Delivered";
        await db.SaveChangesAsync();

        await Product(db, "buyer").Review(1, 2, "Bad fit", "Sizing was too small for the stated measurements.");
        var review = await db.Reviews.SingleAsync();
        await Reviews(db).Reject(review.Id, null, null, null);
        Assert.True((await db.Reviews.SingleAsync()).IsRejected);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Review>>(
            Assert.IsType<ViewResult>(await Product(db, "other").Details("home-shirt"))
                .ViewData["Reviews"]));

        var rejected = Assert.IsType<DeliveredProductReviewViewModel>(
            Assert.IsType<ViewResult>(await Customer(db, "buyer").Review(1, 1)).Model);
        Assert.True(rejected.CanSubmit);
        Assert.Equal("Rejected", rejected.ReviewStatus);
        Assert.Equal(2, rejected.Rating);

        await Customer(db, "buyer").Review(1, 1, 4, "Better after trying it",
            "Actually a comfortable fit after wearing it, and the quality is nice.");
        var changed = await db.Reviews.AsNoTracking().SingleAsync();
        Assert.Equal(review.Id, changed.Id);
        Assert.Equal(4, changed.Rating);
        Assert.False(changed.IsRejected);
        Assert.False(changed.IsApproved);
        Assert.Single(await db.Reviews.ToListAsync());

        var second = Assert.IsType<DeliveredProductReviewViewModel>(
            Assert.IsType<ViewResult>(await Customer(db, "buyer").Review(1, 1)).Model);
        Assert.Equal("Pending", second.ReviewStatus);
        Assert.Equal(1, (await db.AdminNotifications.AsNoTracking()
            .Where(x => x.EventType == "review.pending")).CountAsync());

        await Reviews(db).Approve(review.Id, null, null, null);
        var published = Assert.IsType<ViewResult>(await Product(db, "other").Details("home-shirt"));
        var shown = Assert.Single(Assert.IsAssignableFrom<IEnumerable<Review>>(
            published.ViewData["Reviews"]));
        Assert.Equal(4, shown.Rating);
        Assert.Equal("Better after trying it", shown.Title);
    }

    private static ApplicationDbContext NewDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static MyOrdersController Customer(ApplicationDbContext db, string id) =>
        Setup(new MyOrdersController(db,
            new OrderCancellationService(db, new InventoryStockService(db))), id);
    private static ProductController Product(ApplicationDbContext db, string id) =>
        Setup(new ProductController(db), id);
    private static ReviewsController Reviews(ApplicationDbContext db) =>
        Setup(new ReviewsController(db), "admin");

    private static T Setup<T>(T controller, string id) where T : Controller
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Name, id)
        }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, new DummyTempDataProvider());
        return controller;
    }

    private static void Seed(ApplicationDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "buyer", UserName = "buyer@example.test" },
            new ApplicationUser { Id = "other", UserName = "other@example.test" },
            new ApplicationUser { Id = "admin", UserName = "admin@example.test" });
        db.Categories.Add(new Category { Id = 1, Name = "Jersey", Slug = "jersey" });
        db.Sizes.Add(new Size { Id = 1, Name = "M" });
        db.Colors.Add(new Color { Id = 1, Name = "Blue" });
        db.Products.Add(new Product { Id = 1, Name = "Home Shirt", Slug = "home-shirt",
            CategoryId = 1, BasePrice = 2000m, IsPublished = true });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 1, ProductId = 1, SizeId = 1, ColorId = 1,
            SKU = "JERSEY-HOME-M", Price = 2000m, IsActive = true, StockQuantity = 10
        });
        db.Orders.Add(new Order
        {
            Id = 1, UserId = "buyer", OrderNumber = "PNK-REALORDER", Status = "Pending",
            CustomerName = "Buyer", CustomerEmail = "buyer@example.test",
            CustomerPhone = "01711223344", ShippingAddressLine1 = "Test house",
            ShippingCity = "Dhaka", ShippingCountry = "Bangladesh",
            Subtotal = 2000m, TotalAmount = 2000m
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = 1, OrderId = 1, ProductVariantId = 1,
            ProductName = "Home Shirt", Quantity = 1, UnitPrice = 2000m, LineTotal = 2000m
        });
    }

    private sealed class DummyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
