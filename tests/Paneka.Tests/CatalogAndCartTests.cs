using System.Text.Json;
using Jersey_ecommerce.Controllers;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Shopping;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Services.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using CatalogColor = Jersey_ecommerce.Models.Catalog.Color;
using CatalogSize = Jersey_ecommerce.Models.Catalog.Size;

namespace Paneka.Tests;

public sealed class CatalogAndCartTests
{
    [Theory]
    [InlineData("2026 Retro Shirt", "2026-retro-shirt")]
    [InlineData("  São Paulo FC  ", "sao-paulo-fc")]
    [InlineData("!!", "product")]
    [InlineData("PANENKA -- CLASSIC", "panenka-classic")]
    public void Slug_normalizes_names_without_user_input(string name, string expected)
    {
        Assert.Equal(expected, CatalogSlug.FromName(name, "product"));
    }

    [Fact]
    public async Task Duplicate_names_get_unique_urls_without_a_manual_slug()
    {
        var taken = new HashSet<string> { "home-shirt", "home-shirt-2" };
        var result = await CatalogSlug.UniqueAsync("Home Shirt", "product", 120,
            candidate => Task.FromResult(taken.Contains(candidate)));
        Assert.Equal("home-shirt-3", result);
    }

    [Fact]
    public async Task Shop_stock_filter_excludes_disabled_size_and_color()
    {
        await using var db = NewDb();
        SeedCatalog(db);
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1, SizeId = 1,
            ColorId = 1, SKU = "ACT-1", Price = 1200, StockQuantity = 5, IsActive = true });
        db.ProductVariants.Add(new ProductVariant { Id = 2, ProductId = 2, SizeId = 2,
            ColorId = 1, SKU = "DIS-1", Price = 1300, StockQuantity = 10, IsActive = true });
        await db.SaveChangesAsync();

        var json = await ShopJson(db, inStock: true);
        var products = json.RootElement.GetProperty("products");
        Assert.Equal(1, products.GetArrayLength());
        Assert.Equal("home-shirt", products[0].GetProperty("slug").GetString());
        Assert.Equal(5, products[0].GetProperty("stockQuantity").GetInt64());
    }

    [Fact]
    public async Task Shop_size_and_price_filters_apply_on_the_server()
    {
        await using var db = NewDb();
        SeedCatalog(db);
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1, SizeId = 1,
            ColorId = 1, SKU = "ACT-1", Price = 1200, StockQuantity = 5, IsActive = true });
        db.ProductVariants.Add(new ProductVariant { Id = 2, ProductId = 2, SizeId = 2,
            ColorId = 1, SKU = "DIS-1", Price = 1300, StockQuantity = 10, IsActive = true });
        await db.SaveChangesAsync();

        var size = await ShopJson(db, sizeId: 2);
        Assert.Equal(0, size.RootElement.GetProperty("total").GetInt32());
        var price = await ShopJson(db, minPrice: 1400);
        Assert.Equal(1, price.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Cart_shows_unavailable_variants_and_prevents_stock_bypass()
    {
        await using var db = NewDb();
        SeedCatalog(db);
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1, SizeId = 2,
            ColorId = 1, SKU = "CART-1", Price = 1200, StockQuantity = 12, IsActive = true });
        db.ShoppingCarts.Add(new ShoppingCart { Id = 1, UserId = "buyer-1" });
        db.CartItems.Add(new CartItem { Id = 1, ShoppingCartId = 1, ProductVariantId = 1, Quantity = 2 });
        await db.SaveChangesAsync();

        var service = NewCheckout(db);
        var cart = await service.GetCart("buyer-1");
        Assert.Single(cart.Items);
        Assert.Equal(0, cart.Items[0].StockQuantity);
        await Assert.ThrowsAsync<CheckoutException>(() => service.SetQuantity("buyer-1", 1, 3));
        await Assert.ThrowsAsync<CheckoutException>(() => service.GetCart("buyer-1", validateStock: true));
    }

    [Fact]
    public async Task Cart_recommendations_only_use_purchasable_variants()
    {
        await using var db = NewDb();
        SeedCatalog(db);
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1, SizeId = 1,
            ColorId = 1, SKU = "REC-1", Price = 1200, StockQuantity = 5, IsActive = true });
        db.ProductVariants.Add(new ProductVariant { Id = 2, ProductId = 2, SizeId = 2,
            ColorId = 1, SKU = "REC-2", Price = 1300, StockQuantity = 5, IsActive = true });
        await db.SaveChangesAsync();

        var recommendations = await NewCheckout(db).Recommendations("buyer-1");
        Assert.Single(recommendations);
    }

    private static async Task<JsonDocument> ShopJson(ApplicationDbContext db, bool inStock = false,
        int? sizeId = null, decimal? minPrice = null)
    {
        var controller = new ShopCatalogApiController(db);
        var result = await controller.Get(inStock: inStock, sizeId: sizeId, minPrice: minPrice);
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
    }

    private static CheckoutService NewCheckout(ApplicationDbContext db) =>
        new(db, Options.Create(new CheckoutOptions()), new HttpContextAccessor(),
            new InventoryStockService(db));

    private static ApplicationDbContext NewDb()
    {
        var settings = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("paneka-tests-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(settings);
    }

    private static void SeedCatalog(ApplicationDbContext db)
    {
        db.Categories.Add(new Category { Id = 1, Name = "Jerseys", Slug = "jerseys" });
        db.Sizes.AddRange(
            new CatalogSize { Id = 1, Name = "M", IsActive = true },
            new CatalogSize { Id = 2, Name = "L", IsActive = false });
        db.Colors.Add(new CatalogColor { Id = 1, Name = "Black", IsActive = true });
        db.Products.AddRange(
            new Product { Id = 1, Name = "Home Shirt", Slug = "home-shirt",
                CategoryId = 1, BasePrice = 1200, IsPublished = true },
            new Product { Id = 2, Name = "Archive Shirt", Slug = "archive-shirt",
                CategoryId = 1, BasePrice = 1600, IsPublished = true });
    }
}
