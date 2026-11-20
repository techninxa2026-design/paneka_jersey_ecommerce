using System.Data;
using System.Text.RegularExpressions;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Services.Checkout;

/// <summary>
/// Off-platform bKash transfers: staff independently verify incoming funds.
/// Customer-supplied sender/TrxID/time are claims, NEVER gateway confirmations.
/// </summary>
public sealed class ManualBkashPaymentService(ApplicationDbContext db)
{
    private static readonly Regex Phone = new(@"^01[3-9][0-9]{8}$", RegexOptions.Compiled);
    private static readonly Regex Trx = new(@"^[A-Z0-9-]{7,40}$", RegexOptions.Compiled);
    public const string NumberSettingKey = "payments.bkash_number";
    public const string InstructionsSettingKey = "payments.bkash_instructions";

    public static bool IsValidMobile(string? value) => value is not null && Phone.IsMatch(value);

    public static string DefaultInstructions =>
        "Open your bKash app, use Send Money to the receiving number shown, and send the exact order total. " +
        "Then enter your sender number, transaction ID and transfer time here. Never share your PIN or OTP.";

    public async Task<string?> CurrentReceivingNumberAsync()
    {
        var saved = await db.SiteSettings.AsNoTracking()
            .Where(x => x.Key == NumberSettingKey)
            .Select(x => x.Value).FirstOrDefaultAsync();
        return IsValidMobile(saved) ? saved : null;
    }

    public async Task<string> CurrentInstructionsAsync()
    {
        var value = await db.SiteSettings.AsNoTracking()
            .Where(x => x.Key == InstructionsSettingKey)
            .Select(x => x.Value).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(value) ? DefaultInstructions : value;
    }

    public async Task SubmitAsync(int orderId, string userId, string? rawSender,
        string? rawTransactionId, DateTime localPaidAt)
    {
        var sender = rawSender?.Trim() ?? "";
        var trxId = rawTransactionId?.Trim().ToUpperInvariant() ?? "";
        if (!IsValidMobile(sender))
            throw new CheckoutException("Enter the 11-digit bKash number used to send the money.");
        if (!Trx.IsMatch(trxId))
            throw new CheckoutException("Enter the transaction ID from your actual bKash confirmation (7–40 letters, digits or hyphens).");
        // datetime-local inputs represent Bangladesh wall-clock time (UTC+06:00).
        var claimedUtc = DateTime.SpecifyKind(localPaidAt.AddHours(-6), DateTimeKind.Utc);
        var now = DateTime.UtcNow;
        if (localPaidAt == default || claimedUtc < now.AddDays(-30) || claimedUtc > now.AddMinutes(10))
            throw new CheckoutException("Enter the payment date and time in Bangladesh time (within the last 30 days).");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var payment = await db.Payments.Include(x => x.Order)
            .SingleOrDefaultAsync(x => x.OrderId == orderId && x.Order.UserId == userId);
        if (payment == null || payment.Method != "Bkash")
            throw new CheckoutException("This manual bKash order was not found.");
        if (payment.Order.Status != "Pending" || !IsValidMobile(payment.ManualRecipientNumber))
            throw new CheckoutException("This order is no longer awaiting a manual bKash transfer. Contact support.");
        if (payment.Status == "Submitted")
        {
            if (string.Equals(payment.TransactionReference, trxId, StringComparison.OrdinalIgnoreCase) &&
                payment.ManualSenderNumber == sender)
                return; // repeated browser POST; never create duplicate receipts
            throw new CheckoutException("A transfer is already awaiting verification. Wait for staff review before submitting another ID.");
        }
        if (payment.Status is not ("Pending" or "Rejected"))
            throw new CheckoutException("This payment is no longer open for manual submission.");

        var used = await db.Payments.AsNoTracking().AnyAsync(x => x.Id != payment.Id &&
            x.Method == "Bkash" && x.TransactionReference == trxId) ||
            await db.PaymentTransactions.AsNoTracking().AnyAsync(x =>
                x.PaymentId != payment.Id && x.TransactionType == "ManualBkash" &&
                x.ProviderTransactionId == trxId);
        if (used) throw new CheckoutException("That transaction ID has already been recorded on another order. Contact support.");

        payment.ManualSenderNumber = sender;
        payment.TransactionReference = trxId;
        payment.ManualClaimedPaidAt = claimedUtc;
        payment.ManualSubmittedAt = now;
        payment.ManualReviewedAt = null;
        payment.ManualReviewedByUserId = null;
        payment.ManualReviewNote = null;
        payment.Provider = "bKash (manual transfer)";
        payment.Status = "Submitted"; // NOT Paid
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            PaymentId = payment.Id, TransactionType = "ManualBkash",
            Status = "Submitted", Reference = sender, ProviderTransactionId = trxId,
            Amount = payment.Amount, CreatedAt = now
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task VerifyAsync(int paymentId, string adminId, string? reviewNote)
    {
        var note = ReviewNote(reviewNote);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var payment = await db.Payments.Include(p => p.Order)
            .SingleOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null || payment.Method != "Bkash" || payment.ManualSubmittedAt == null)
            throw new CheckoutException("Only manually submitted bKash payments can be verified here.");
        if (payment.Status != "Submitted" || payment.Order.Status != "Pending")
            throw new CheckoutException("Payment or order status has already changed. Refresh the review page.");
        if (string.IsNullOrWhiteSpace(payment.TransactionReference) || !IsValidMobile(payment.ManualSenderNumber))
            throw new CheckoutException("Sender and transaction details are missing.");

        var now = DateTime.UtcNow;
        payment.Status = "Paid";
        payment.PaidAt = now;
        payment.ManualReviewedAt = now;
        payment.ManualReviewedByUserId = adminId;
        payment.ManualReviewNote = note;
        payment.Order.Status = "Confirmed";
        payment.Order.PaidAt = now;
        payment.Order.UpdatedAt = now;

        db.PaymentTransactions.Add(new PaymentTransaction
        {
            PaymentId = payment.Id, TransactionType = "ManualBkash",
            Status = "Verified", ProviderTransactionId = payment.TransactionReference,
            Reference = payment.ManualSenderNumber, Amount = payment.Amount,
            CreatedAt = now, CompletedAt = now
        });
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = payment.OrderId, Status = "Confirmed", ChangedByUserId = adminId,
            Note = "Manual bKash transfer independently verified by staff.",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RejectAsync(int paymentId, string adminId, string? reviewNote)
    {
        var note = ReviewNote(reviewNote);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var payment = await db.Payments.Include(p => p.Order)
            .SingleOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null || payment.Method != "Bkash" || payment.Status != "Submitted" ||
            payment.Order.Status != "Pending" || payment.ManualSubmittedAt == null)
            throw new CheckoutException("Only pending manual bKash submissions can be rejected.");

        var now = DateTime.UtcNow;
        payment.Status = "Rejected";
        payment.ManualReviewedAt = now;
        payment.ManualReviewedByUserId = adminId;
        payment.ManualReviewNote = note;
        // Retain the old sender/trx/claimed time for the audit. Resubmission
        // will replace the current claim but prior attempts remain in the ledger.
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            PaymentId = payment.Id, TransactionType = "ManualBkash",
            Status = "Rejected", ProviderTransactionId = payment.TransactionReference,
            Reference = payment.ManualSenderNumber, Amount = payment.Amount,
            FailureReason = note, CreatedAt = now, CompletedAt = now
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static string ReviewNote(string? raw)
    {
        var note = raw?.Trim() ?? "";
        if (note.Length is < 10 or > 450)
            throw new CheckoutException("Record at least 10 characters explaining your independent verification or rejection (maximum 450).");
        return note;
    }
}
