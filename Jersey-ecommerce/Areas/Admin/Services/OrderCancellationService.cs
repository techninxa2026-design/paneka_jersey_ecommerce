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
    public async Task CancelUnpaidPendingOrder(int orderId, string userId, bool asAdmin = false,
        string? financeNote = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await db.Orders.Include(o => o.Payment).Include(o => o.Shipment)
            .Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == orderId && (asAdmin || o.UserId == userId));
        if (order == null) throw new CheckoutException("Order not found.");
        // A bKash pre-reservation can remain unpaid if the customer closes
        // the popup. Only staff may release stock, after checking the actual
        // merchant wallet and recording the independent reconciliation.
        var note = financeNote?.Trim() ?? "";
        var isManualBkashReconciled = asAdmin &&
            order.Payment?.Method == "Bkash" &&
            order.Payment.Status is ("Pending" or "Rejected") &&
            note.Length is >= 10 and <= 450;
        if (order.Payment?.Method == "ManualAdvance" ||
            (order.Payment?.Method == "Bkash" && !isManualBkashReconciled))
            throw new CheckoutException("A bKash order requires finance confirmation that no transfer was received before stock is released.");
        if (order.Status != "Pending" || order.Payment == null ||
            (order.Payment.Status != "Pending" &&
             !(isManualBkashReconciled && order.Payment.Status == "Rejected")) ||
            order.Shipment?.Status is not (null or "Pending"))
            throw new CheckoutException("Only pending, unpaid, unshipped orders can be cancelled after reconciliation.");

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
        if (isManualBkashReconciled)
        {
            order.Payment.ManualReviewedAt = cancelledAt;
            order.Payment.ManualReviewedByUserId = userId;
            order.Payment.ManualReviewNote = note;
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                PaymentId = order.Payment.Id,
                TransactionType = "ManualBkash",
                Status = "Cancelled",
                Amount = order.Payment.Amount,
                Reference = order.Payment.ManualSenderNumber,
                ProviderTransactionId = order.Payment.TransactionReference,
                FailureReason = "Staff reconciled no funds before cancellation: " + note,
                CreatedAt = cancelledAt,
                CompletedAt = cancelledAt
            });
        }
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
            Note = isManualBkashReconciled
                ? "Manual bKash order cancelled after independent no-funds reconciliation; stock and coupon restored. "
                    + note[..Math.Min(note.Length, 160)]
                : "Unpaid order cancelled; stock restored and coupon allowance released.",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
