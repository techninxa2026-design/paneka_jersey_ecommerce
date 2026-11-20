using System.Security.Claims;
using Jersey_ecommerce.Areas.Admin.Controllers;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Paneka.Tests;

/// <summary>
/// Smoke-executes the actual 16 admin sidebar index actions against a real
/// relational EF Core store. Route/authorization/anti-CSRF contracts are
/// separately checked in AdminSidebarContractsTests.
/// </summary>
public sealed class AdminSidebarIndexSmokeTests
{
    [Fact]
    public async Task All_sixteen_admin_sidebar_modules_load_real_index_models()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        // A real store-backed UserManager is required by Customers.Index
        // to exclude Admin-role users (rather than mocking away the query).
        using var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var results = new (string Name, Task<IActionResult> Result)[]
        {
            ("Dashboard", Setup(new DashboardController(db)).Index()),
            ("Notifications", Setup(new NotificationsController(db,
                new AdminNotificationReader(db))).Index()),
            ("Products", Setup(new ProductsController(db, null!,
                NullLogger<ProductsController>.Instance)).Index(null)),
            ("Categories", Setup(new CategoriesController(db, null!,
                NullLogger<CategoriesController>.Instance)).Index(null)),
            ("Brands", Setup(new BrandsController(db, null!,
                NullLogger<BrandsController>.Instance)).Index(null, null)),
            ("Collections", Setup(new CollectionsController(db, null!,
                NullLogger<CollectionsController>.Instance)).Index(null, null)),
            ("Sizes & Colors", Setup(new VariantsController(db, null!,
                NullLogger<VariantsController>.Instance)).Index(null, null)),
            ("Orders", Setup(new OrdersController(db,
                NullLogger<OrdersController>.Instance)).Index(null, null, null, null)),
            ("Payments", Setup(new PaymentsController(db)).Index(null, null, null, null, null)),
            ("Verify bKash", Setup(new CheckoutPaymentReviewController(db,
                new ManualBkashPaymentService(db))).Index()),
            ("Shipments", Setup(new ShipmentsController(db,
                NullLogger<ShipmentsController>.Instance)).Index(null, null)),
            ("Customers", Setup(new CustomersController(db, userManager,
                NullLogger<CustomersController>.Instance)).Index(null)),
            ("Reviews", Setup(new ReviewsController(db)).Index(null)),
            ("Coupons", Setup(new CouponsController(db,
                NullLogger<CouponsController>.Instance)).Index(null)),
            ("Banners & Slides", Setup(new BannersController(db, null!,
                NullLogger<BannersController>.Instance)).Index()),
            ("Settings", Setup(new SettingsController(db, null!,
                NullLogger<SettingsController>.Instance)).Index())
        };

        Assert.Equal(16, results.Length);
        foreach (var (name, result) in results)
        {
            var response = await result;
            Assert.True(response is ViewResult view && view.Model != null,
                $"{name} did not return a populated admin dashboard model.");
        }
    }

    private static T Setup<T>(T controller) where T : Controller
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, new DummyTempDataProvider());
        return controller;
    }

    private sealed class DummyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
