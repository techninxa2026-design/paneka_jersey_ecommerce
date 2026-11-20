using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class ReviewsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const int PageSize = 12;

        public ReviewsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search, string status = "all", int? rating = null, int page = 1)
        {
            search = search?.Trim();
            if (search?.Length > 100)
                search = search[..100];

            status = (status ?? "all").Trim().ToLowerInvariant();
            if (status != "approved" && status != "pending" && status != "rejected")
                status = "all";

            if (rating.HasValue && (rating.Value < 1 || rating.Value > 5))
                rating = null;

            IQueryable<Review> all = _context.Reviews.AsNoTracking();
            var model = new ReviewsIndexViewModel
            {
                Search = search,
                Status = status,
                Rating = rating,
                PageSize = PageSize,
                TotalReviews = await all.CountAsync(),
                ApprovedReviews = await all.CountAsync(r => r.IsApproved && !r.IsRejected),
                PendingReviews = await all.CountAsync(r => !r.IsApproved && !r.IsRejected),
                RejectedReviews = await all.CountAsync(r => r.IsRejected),
                AverageRating = await all.Where(r => r.IsApproved && !r.IsRejected).AverageAsync(r => (double?)r.Rating) ?? 0
            };

            IQueryable<Review> query = all;
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(r =>
                    r.Product.Name.Contains(search) ||
                    (r.User.UserName != null && r.User.UserName.Contains(search)) ||
                    (r.User.Email != null && r.User.Email.Contains(search)) ||
                    (r.Title != null && r.Title.Contains(search)) ||
                    r.Comment.Contains(search));
            }

            query = status switch
            {
                "approved" => query.Where(r => r.IsApproved && !r.IsRejected),
                "rejected" => query.Where(r => r.IsRejected),
                "pending" => query.Where(r => !r.IsApproved && !r.IsRejected),
                _ => query
            };

            if (rating.HasValue)
                query = query.Where(r => r.Rating == rating.Value);

            model.FilteredCount = await query.CountAsync();
            model.Page = Math.Clamp(page, 1, model.TotalPages);
            model.Reviews = await ProjectReviews(query)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .Skip((model.Page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var review = await ProjectReviews(_context.Reviews.AsNoTracking())
                .FirstOrDefaultAsync(r => r.Id == id);
            return review == null ? NotFound() : View(review);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> Approve(int id, string? search, string? status, int? rating, int page = 1)
            => SetStatus(id, true, false, "Review approved successfully.", search, status, rating, page);

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> Reject(int id, string? search, string? status, int? rating, int page = 1)
            => SetStatus(id, false, true, "Review rejected successfully.", search, status, rating, page);

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> Pending(int id, string? search, string? status, int? rating, int page = 1)
            => SetStatus(id, false, false, "Review moved to pending.", search, status, rating, page);

        private async Task<IActionResult> SetStatus(
            int id, bool approved, bool rejected, string message,
            string? search, string? status, int? rating, int page)
        {
            var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null) return NotFound();

            review.IsApproved = approved;
            review.IsRejected = rejected;
            await _context.SaveChangesAsync();
            TempData["Success"] = message;
            return BackToIndex(search, status, rating, page);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(
            int id, string? search, string? status, int? rating, int page = 1)
        {
            var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null) return NotFound();

            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Review deleted permanently.";
            return BackToIndex(search, status, rating, page);
        }

        private static IQueryable<ReviewRowViewModel> ProjectReviews(IQueryable<Review> query)
        {
            return query.Select(r => new ReviewRowViewModel
            {
                Id = r.Id,
                ProductId = r.ProductId,
                ProductName = r.Product.Name,
                UserId = r.UserId,
                CustomerName = r.User.UserName ?? r.User.Email ?? "Customer",
                Email = r.User.Email,
                Rating = r.Rating,
                Title = r.Title,
                Comment = r.Comment,
                IsApproved = r.IsApproved,
                IsRejected = r.IsRejected,
                CreatedAt = r.CreatedAt
            });
        }

        private IActionResult BackToIndex(string? search, string? status, int? rating, int page)
        {
            return RedirectToAction(nameof(Index), new { search, status, rating, page });
        }
    }
}
