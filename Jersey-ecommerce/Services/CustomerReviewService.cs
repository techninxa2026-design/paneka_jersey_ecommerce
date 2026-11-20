using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Customers;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Services;

/// <summary>
/// Single set of purchase-verified review rules for both the product details
/// page and the delivered-order review form. Reviews are one per user/product.
/// Rejected reviews may be corrected and resubmitted for staff moderation.
/// </summary>
public static class CustomerReviewService
{
    public const int MaxCommentLength = 2000;

    public static async Task<ReviewSubmissionResult> SubmitAsync(
        ApplicationDbContext db, string userId, int productId, int rating,
        string? title, string? comment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ReviewSubmissionResult.Invalid("Sign in before reviewing a purchase.");

        var trimmedComment = comment?.Trim() ?? "";
        var trimmedTitle = title?.Trim();
        if (rating is < 1 or > 5 || trimmedComment.Length is < 10 or > MaxCommentLength ||
            trimmedTitle?.Length > 180)
            return ReviewSubmissionResult.Invalid(
                "Choose 1–5 stars and write at least 10 characters (maximum 2,000). Review titles must be under 180 characters.");

        // Never trust the page, order ID or product ID supplied by the browser.
        var delivered = await db.OrderItems.AsNoTracking().AnyAsync(i =>
            i.ProductVariant != null && i.ProductVariant.ProductId == productId &&
            i.Order.UserId == userId && i.Order.Status == "Delivered", ct);
        if (!delivered)
            return ReviewSubmissionResult.Invalid(
                "A review can only be submitted by the customer after delivery.");

        var exists = await db.Products.AsNoTracking().AnyAsync(x => x.Id == productId, ct);
        if (!exists)
            return ReviewSubmissionResult.Invalid("This product is no longer available for reviews.");

        var review = await db.Reviews
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductId == productId, ct);

        if (review is not null && review.IsApproved && !review.IsRejected)
            return ReviewSubmissionResult.Invalid("Your review is already published.");

        if (review is not null && !review.IsRejected)
            return ReviewSubmissionResult.Invalid("Your review is awaiting administrator approval.");

        var isRevision = review is not null;
        if (review is null)
        {
            review = new Review { UserId = userId, ProductId = productId };
            db.Reviews.Add(review);
        }
        review.Rating = rating;
        review.Title = string.IsNullOrWhiteSpace(trimmedTitle) ? null : trimmedTitle;
        review.Comment = trimmedComment;
        review.IsApproved = false;
        review.IsRejected = false;
        if (isRevision)
            review.CreatedAt = DateTime.UtcNow; // latest moderation submission time

        try
        {
            await db.SaveChangesAsync(ct);
            return new ReviewSubmissionResult(true,
                isRevision ? "Your revised review was submitted to the admin moderation queue."
                    : "Thank you! Your review was sent to PANENKA for approval.");
        }
        catch (DbUpdateException)
        {
            // The unique (UserId, ProductId) index makes simultaneous submits safe.
            db.Entry(review).State = EntityState.Detached;
            return ReviewSubmissionResult.Invalid(
                "We could not save this review. Check whether you already submitted one.");
        }
    }
}

public sealed record ReviewSubmissionResult(bool Saved, string Message)
{
    public static ReviewSubmissionResult Invalid(string message) => new(false, message);
}
