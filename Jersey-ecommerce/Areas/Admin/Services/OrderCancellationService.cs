using System.Data;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Services;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Services.Checkout;

public sealed class OrderCancellationService(ApplicationDbContext db, InventoryStockService inventory)
{
    // Cancel only an unpaid order that has not entered fulfillment.
    // Runs all stock restores in the same transaction as the status change.
    public async Task CancelUnpaidPendingOrder(int orderId, string userId, bool asAdmin = false)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await db.Orders.Include(o => o.Payment).Include(o => o.Shipment)
            .Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == orderId && (asAdmin || o.UserId == userId));
        if (order == null) throw new CheckoutException("Order not found.");
        // A manual bKash claim may have been transferred externally even
        // before the customer submitted a TrxID. The generic unpaid-order
        // cancellation path must not restore reserved stock without finance
        // reconciling possible wallet receipts/refunds first.
        if (order.Payment?.Method is "ManualAdvance" or "Bkash")
            throw new CheckoutException("Advance and bKash orders require independent finance reconciliation before cancellation. Use staff support.");
        if (order.Status != "Pending" || order.Payment == null || order.Payment.Status != "Pending" ||
            order.Shipment?.Status is not (null or "Pending"))
            throw new CheckoutException("Only pending, unpaid, unshipped orders can be cancelled here. Contact support.");

        foreach (var item in order.Items)
        {
            if (!item.ProductVariantId.HasValue)
                throw new CheckoutException("This order has an item without a variant; an admin must restore its stock manually.");
            await inventory.RestoreCancelledOrderAsync(item.ProductVariantId.Value, item.Quantity, order.Id);
        }
        var cancelledAt = DateTime.UtcNow;
        order.Status = "Cancelled";
        order.UpdatedAt = cancelledAt;
        order.CancelledAt = cancelledAt;
        order.Payment.Status = "Cancelled";
        if (order.Shipment != null) order.Shipment.Status = "Cancelled";
        var usage = await db.CouponUsages.FirstOrDefaultAsync(u => u.OrderId == order.Id);
        if (usage != null)
        {
            await db.Coupons.Where(c => c.Id == usage.CouponId && c.UsageCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsageCount, c => c.UsageCount - 1));
            db.CouponUsages.Remove(usage);
        }
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId, Status = "Cancelled", ChangedByUserId = userId,
            Note = "Unpaid order cancelled; stock restored and coupon allowance released.",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
