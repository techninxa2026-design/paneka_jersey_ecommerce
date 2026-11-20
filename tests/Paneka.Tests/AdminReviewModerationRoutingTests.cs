using System.Reflection;
using System.Security.Claims;
using Jersey_ecommerce.Areas.Admin.Controllers;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Customers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Paneka.Tests;

/// <summary>
/// Regression protection against HTTP 405 caused by Razor forms silently
/// POSTing back to GET /Admin/Reviews/Details/{id} without a form action.
/// </summary>
public sealed class AdminReviewModerationRoutingTests
{
    [Fact]
    public void Admin_view_root_enables_tag_helpers_and_review_forms_have_explicit_post_destinations()
    {
        var imports = ReadSource("Jersey-ecommerce/Areas/Admin/Views/_ViewImports.cshtml");
        Assert.Contains("@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers", imports);

        foreach (var (path, idExpression) in new[]
        {
            ("Jersey-ecommerce/Areas/Admin/Views/Reviews/Details.cshtml", "Model.Id"),
            ("Jersey-ecommerce/Areas/Admin/Views/Reviews/Index.cshtml", "review.Id")
        })
        {
            var markup = ReadSource(path);
            foreach (var action in new[] { "Approve", "Reject", "Pending", "Delete" })
            {
                Assert.Contains(
                    $"<form action=\"@Url.Action(\"{action}\", \"Reviews\", new {{ area = \"Admin\", id = {idExpression} }})\" method=\"post\"",
                    markup);
            }
            Assert.Contains("@Html.AntiForgeryToken()", markup);
            Assert.DoesNotContain("<form asp-area=\"Admin\" asp-controller=\"Reviews\" asp-action=", markup);
        }
    }

    [Theory]
    [InlineData("Approve")]
    [InlineData("Reject")]
    [InlineData("Pending")]
    [InlineData("Delete")]
    public void All_review_moderation_routes_accept_post_and_require_csrf(string action)
    {
        var method = typeof(ReviewsController).GetMethod(action);
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var details = typeof(ReviewsController).GetMethod(nameof(ReviewsController.Details));
        Assert.NotNull(details!.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(details.GetCustomAttribute<HttpPostAttribute>());
    }

    [Fact]
    public async Task Approve_reject_restore_pending_and_delete_change_real_sqlite_review_records()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new ApplicationUser
        {
            Id = "customer-one", UserName = "customer@panenka.test", Email = "customer@panenka.test"
        });
        db.Categories.Add(new Category { Id = 1, Name = "Jerseys", Slug = "jerseys" });
        db.Products.Add(new Product
        {
            Id = 1, CategoryId = 1, Name = "Official Jersey",
            Slug = "official-jersey", BasePrice = 2500m, IsPublished = true
        });
        db.Reviews.Add(new Review
        {
            Id = 1, ProductId = 1, UserId = "customer-one", Rating = 5,
            Title = "Great Jersey", Comment = "Very good quality and fit",
            IsApproved = false, IsRejected = false
        });
        await db.SaveChangesAsync();

        var controller = new ReviewsController(db);
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, new TempDataStub());

        Assert.IsType<ViewResult>(await controller.Details(1));

        var approve = Assert.IsType<RedirectToActionResult>(
            await controller.Approve(1, null, null, null));
        Assert.Equal("Index", approve.ActionName);
        Assert.True((await db.Reviews.AsNoTracking().SingleAsync()).IsApproved);

        var reject = Assert.IsType<RedirectToActionResult>(
            await controller.Reject(1, null, null, null));
        Assert.Equal("Index", reject.ActionName);
        var rejected = await db.Reviews.AsNoTracking().SingleAsync();
        Assert.True(rejected.IsRejected);
        Assert.False(rejected.IsApproved);

        var pending = Assert.IsType<RedirectToActionResult>(
            await controller.Pending(1, null, null, null));
        Assert.Equal("Index", pending.ActionName);
        var restored = await db.Reviews.AsNoTracking().SingleAsync();
        Assert.False(restored.IsRejected);
        Assert.False(restored.IsApproved);

        var deleted = Assert.IsType<RedirectToActionResult>(
            await controller.Delete(1, null, null, null));
        Assert.Equal("Index", deleted.ActionName);
        Assert.Empty(await db.Reviews.AsNoTracking().ToListAsync());
    }

    private static string ReadSource(string relativePath)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var step = 0; root != null && step < 12; root = root.Parent, step++)
        {
            var path = Path.Combine(root.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("Repository source unavailable for review route contract.", relativePath);
    }

    private sealed class TempDataStub : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
            => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
