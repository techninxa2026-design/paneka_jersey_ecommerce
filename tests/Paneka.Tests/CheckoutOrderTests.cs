using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Checkout;
using Jersey_ecommerce.Models.Shopping;
using Jersey_ecommerce.Services;
using Jersey_ecommerce.Services.Checkout;
using Jersey_ecommerce.ViewModels.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using CatalogColor = Jersey_ecommerce.Models.Catalog.Color;
using CatalogSize = Jersey_ecommerce.Models.Catalog.Size;

namespace Paneka.Tests;

public sealed class CheckoutOrderTests
{
    [Fact]
    public async Task Cash_on_delivery_order_reserves_stock_and_is_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new ApplicationUser { Id = "test-buyer", FullName = "Test Buyer",
            UserName = "buyer@example.test", Email = "buyer@example.test" });
        db.Categories.Add(new Category { Id = 1, Name = "Jerseys", Slug = "jerseys" });
        db.Sizes.Add(new CatalogSize { Id = 1, Name = "M" });
        db.Colors.Add(new CatalogColor { Id = 1, Name = "Black" });
        db.Products.Add(new Product { Id = 1, Name = "Black Jersey", Slug = "black-jersey",
            CategoryId = 1, BasePrice = 1500m, IsPublished = true });
        db.ProductVariants.Add(new ProductVariant { Id = 1, ProductId = 1,
            SizeId = 1, ColorId = 1, SKU = "BLACK-M", StockQuantity = 8,
            Price = 1500m, IsActive = true });
        db.ShoppingCarts.Add(new ShoppingCart { Id = 1, UserId = "test-buyer" });
        db.CartItems.Add(new CartItem { ShoppingCartId = 1, ProductVariantId = 1, Quantity = 2 });
        await db.SaveChangesAsync();

        var stock = new InventoryStockService(db);
        var checkout = new CheckoutService(db, Options.Create(new CheckoutOptions()),
            new HttpContextAccessor(), stock);
        var delivery = new DeliveryInput { FullName = "Test Buyer", Email = "buyer@example.test",
            Phone = "01700000000", Region = "Dhaka", District = "Dhaka",
            City = "Dhaka", AddressLine1 = "House 1 Road 2" };
        var payment = new PlaceOrderInput { PaymentMethod = "COD",
            SubmissionToken = Guid.NewGuid().ToString("N") };

        var id = await checkout.PlaceOrder("test-buyer", delivery, payment);
        var duplicateId = await checkout.PlaceOrder("test-buyer", delivery, payment);
        Assert.Equal(id, duplicateId);
        Assert.Single(await db.Orders.ToListAsync());

        var order = await db.Orders.AsNoTracking().Include(x => x.Payment)
            .Include(x => x.Shipment).Include(x => x.Items).SingleAsync();
        Assert.Equal(3000m, order.TotalAmount);
        Assert.Equal("CashOnDelivery", order.Payment?.Method);
        Assert.Equal("Pending", order.Payment?.Status);
        Assert.Equal("Pending", order.Shipment?.Status);
        Assert.Equal(2, Assert.Single(order.Items).Quantity);
        Assert.Equal(6, await db.ProductVariants.AsNoTracking()
            .Where(v => v.Id == 1).Select(v => v.StockQuantity).SingleAsync());
        Assert.Empty(await db.CartItems.ToListAsync());
        Assert.Single(await db.InventoryTransactions.Where(t => t.OrderId == id && t.Type == "Sale").ToListAsync());

        await new OrderCancellationService(db, stock).CancelUnpaidPendingOrder(id, "test-buyer");
        Assert.Equal(8, await db.ProductVariants.AsNoTracking()
            .Where(v => v.Id == 1).Select(v => v.StockQuantity).SingleAsync());
        var cancelled = await db.Orders.AsNoTracking().Include(x => x.Shipment)
            .Include(x => x.Payment).SingleAsync();
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Cancelled", cancelled.Payment?.Status);
        Assert.Equal("Cancelled", cancelled.Shipment?.Status);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.Single(await db.InventoryTransactions.Where(t => t.OrderId == id && t.Type == "Restock").ToListAsync());
        await Assert.ThrowsAsync<CheckoutException>(() =>
            new OrderCancellationService(db, stock).CancelUnpaidPendingOrder(id, "test-buyer"));
    }

    [Fact]
    public async Task A_checkout_cannot_succeed_with_an_invalid_delivery_region()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new CheckoutService(db, Options.Create(new CheckoutOptions()),
            new HttpContextAccessor(), new InventoryStockService(db));
        var data = new DeliveryInput { FullName = "Customer", Email = "buyer@example.test",
            Phone = "01700000000", Region = "Mars", District = "Dhaka",
            City = "Dhaka", AddressLine1 = "Test Road" };
        await Assert.ThrowsAsync<CheckoutException>(() => service.PlaceOrder("buyer", data,
            new PlaceOrderInput { PaymentMethod = "COD", SubmissionToken = Guid.NewGuid().ToString("N") }));
    }
}
