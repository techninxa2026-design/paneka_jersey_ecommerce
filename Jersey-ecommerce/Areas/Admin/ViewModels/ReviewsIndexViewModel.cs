using System;
using System.Collections.Generic;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public class ReviewRowViewModel
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductSlug { get; set; } = string.Empty;
        public bool ProductIsPublished { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public int Rating { get; set; }
        public string? Title { get; set; }
        public string Comment { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public bool IsRejected { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ReviewsIndexViewModel
    {
        public List<ReviewRowViewModel> Reviews { get; set; } = new();
        public string? Search { get; set; }
        public string Status { get; set; } = "all";
        public int? Rating { get; set; }
        public int TotalReviews { get; set; }
        public int ApprovedReviews { get; set; }
        public int PendingReviews { get; set; }
        public int RejectedReviews { get; set; }
        public double AverageRating { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 12;
        public int FilteredCount { get; set; }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)PageSize));
    }
}
