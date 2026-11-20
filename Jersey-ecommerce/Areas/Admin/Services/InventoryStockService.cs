using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Services
{
    /// <summary>
    /// Invoke while creating an order INSIDE the caller's DB transaction.
    /// This service does not begin/commit a transaction or call SaveChanges.
    /// Caller must SaveChanges and commit only when order and inventory logs succeed.
    /// </summary>
    public sealed class InventoryStockService
    {
        private readonly ApplicationDbContext _db;

        public InventoryStockService(ApplicationDbContext db) => _db = db;

        /// <summary>
        /// Atomically decrements a single selected size/color variant.
        /// orderId must be an existing/saved Order.Id (in this transaction).
        /// Throws if unpublished, inactive or insufficient stock.
        /// Must be called only once per order item; checkout must enforce
        /// order/request idempotency separately.
        /// </summary>
        public async Task DecreaseForOrderAsync(
            int variantId, int quantity, int orderId, CancellationToken cancellationToken = default)
        {
            if (variantId <= 0 || orderId <= 0 || quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity),
                    "Variant ID, order ID, and quantity must all be positive.");

            // Helps catch duplicate application requests, but is NOT a replacement
            // for order/request idempotency during concurrent checkouts.
            if (await _db.InventoryTransactions.AsNoTracking().AnyAsync(x =>
                    x.OrderId == orderId && x.ProductVariantId == variantId && x.Type == "Sale",
                    cancellationToken))
                throw new InvalidOperationException("Stock already deducted for this order and variant.");

            // SQL UPDATE .. WHERE StockQuantity >= quantity ensures that two
            // concurrent buyers cannot both purchase the last unit.
            var changed = await _db.ProductVariants
                .Where(v => v.Id == variantId && v.IsActive &&
                            v.Product.IsPublished && v.Size.IsActive && v.Color.IsActive &&
                            v.StockQuantity >= quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(v => v.StockQuantity, v => v.StockQuantity - quantity),
                    cancellationToken);
            if (changed != 1)
                throw new InvalidOperationException(
                    "This size/color is unavailable or the requested quantity is out of stock.");

            var after = await _db.ProductVariants.AsNoTracking()
                .Where(v => v.Id == variantId)
                .Select(v => v.StockQuantity)
                .SingleAsync(cancellationToken);

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductVariantId = variantId,
                OrderId = orderId,
                Type = "Sale",
                QuantityChange = -quantity,
                StockBefore = after + quantity,
                StockAfter = after,
                Note = "Stock deducted when order was placed",
                CreatedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Optional: restore stock when an order is CANCELLED or refunded.
        /// Execute once within the same DB transaction that changes order status.
        /// Do not call for an order unless its stock was deducted previously.
        /// </summary>
        public async Task RestoreCancelledOrderAsync(
            int variantId, int quantity, int orderId, CancellationToken cancellationToken = default)
        {
            if (variantId <= 0 || orderId <= 0 || quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));

            if (!await _db.InventoryTransactions.AsNoTracking().AnyAsync(x =>
                    x.OrderId == orderId && x.ProductVariantId == variantId && x.Type == "Sale",
                    cancellationToken))
                throw new InvalidOperationException("No order stock deduction exists to restore.");

            if (await _db.InventoryTransactions.AsNoTracking().AnyAsync(x =>
                    x.OrderId == orderId && x.ProductVariantId == variantId && x.Type == "Restock",
                    cancellationToken))
                throw new InvalidOperationException("Stock for this order and variant was already restored.");

            var changed = await _db.ProductVariants
                .Where(v => v.Id == variantId && v.StockQuantity <= int.MaxValue - quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(v => v.StockQuantity, v => v.StockQuantity + quantity),
                    cancellationToken);
            if (changed != 1)
                throw new InvalidOperationException("Cannot restore stock due to missing variant or quantity overflow.");

            var after = await _db.ProductVariants.AsNoTracking()
                .Where(v => v.Id == variantId)
                .Select(v => v.StockQuantity)
                .SingleAsync(cancellationToken);

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductVariantId = variantId,
                OrderId = orderId,
                Type = "Restock",
                QuantityChange = quantity,
                StockBefore = after - quantity,
                StockAfter = after,
                Note = "Stock restored after order cancellation",
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
