using Jersey_ecommerce.Areas.Admin.Controllers;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Paneka.Tests;

public sealed class AdminUrlTests
{
    [Fact]
    public async Task Admin_brand_creation_ignores_user_slug_and_generates_unique_url()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await using var db = NewDb(conn);
        await db.Database.EnsureCreatedAsync();
        db.Brands.Add(new Brand { Name = "Classic", Slug = "classic" });
        await db.SaveChangesAsync();
        var admin = new BrandsController(db, null!, NullLogger<BrandsController>.Instance);
        var result = await admin.Create(new BrandFormViewModel {
            Name = "Classic", Slug = "inject-a-user-url", IsActive = true
        });
        Assert.IsType<RedirectToActionResult>(result);
        var created = await db.Brands.SingleAsync(x => x.Slug == "classic-2");
        Assert.Equal("Classic", created.Name);
    }

    [Fact]
    public async Task Admin_collection_rename_generates_url_automatically()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await using var db = NewDb(conn);
        await db.Database.EnsureCreatedAsync();
        var admin = new CollectionsController(db, null!, NullLogger<CollectionsController>.Instance);
        var result = await admin.Create(new CollectionFormViewModel {
            Name = "Retro Icons", Slug = "malicious-slug", IsActive = true
        });
        Assert.IsType<RedirectToActionResult>(result);
        var created = await db.Collections.SingleAsync();
        Assert.Equal("retro-icons", created.Slug);
    }

    private static ApplicationDbContext NewDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
}
