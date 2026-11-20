using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public sealed class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        // Bangladesh has a fixed UTC+06:00 offset (no DST).
        private static readonly TimeSpan StoreOffset = TimeSpan.FromHours(6);

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int days = 30, CancellationToken ct = default)
        {
            days = days is 7 or 30 or 90 ? days : 30;
            var nowUtc = DateTime.UtcNow;
            var localNow = nowUtc.Add(StoreOffset);
            var startLocal = localNow.Date.AddDays(1 - days);
            var startUtc = startLocal.Subtract(StoreOffset);
            var previousStartUtc = startUtc.AddDays(-days);
            var todayStartUtc = localNow.Date.Subtract(StoreOffset);

            // Only aggregate orders in the two comparison windows are projected into memory.
            // The query avoids fetching customer / payment / item graphs.
            var periodOrderSnapshots = await _context.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= previousStartUtc && o.CreatedAt <= nowUtc)
                .Select(o => new
                {
                    o.CreatedAt,
                    o.Status,
                    o.TotalAmount
                })
                .ToListAsync(ct);

            var currentOrders = periodOrderSnapshots
                .Where(o => o.CreatedAt >= startUtc).ToList();
            var completedCurrent = currentOrders
                .Where(o => string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var completedPrevious = periodOrderSnapshots
                .Where(o => o.CreatedAt < startUtc &&
                            string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var model = new DashboardViewModel
            {
                Days = days,
                LocalNow = localNow,
                RangeStartLocal = startLocal,
                RangeEndLocal = localNow.Date,

                TotalProducts = await _context.Products.CountAsync(ct),
                TotalCategories = await _context.Categories.CountAsync(ct),
                TotalBrands = await _context.Brands.CountAsync(ct),
                TotalOrders = await _context.Orders.CountAsync(ct),
                PendingOrders = await _context.Orders.CountAsync(o => o.Status == "Pending", ct),
                TotalCustomers = await _context.Users.CountAsync(ct),
                TotalCoupons = await _context.Coupons.CountAsync(ct),
                TotalReviews = await _context.Reviews.CountAsync(ct),
                TotalShipments = await _context.Shipments.CountAsync(ct),

                // Important: count shared Product stock, NOT the older variant stock.
                PublishedProducts = await _context.Products.CountAsync(p => p.IsPublished, ct),
                DraftProducts = await _context.Products.CountAsync(p => !p.IsPublished, ct),
                FeaturedProducts = await _context.Products.CountAsync(p => p.IsFeatured, ct),
                AvailableStockUnits = await _context.Products
                    .SumAsync(p => (long?)p.StockQuantity, ct) ?? 0L,
                LowStockProducts = await _context.Products
                    .CountAsync(p => p.IsPublished && p.StockQuantity > 0 && p.StockQuantity <= 5, ct),
                OutOfStockProducts = await _context.Products
                    .CountAsync(p => p.IsPublished && p.StockQuantity == 0, ct),
                ProductsWithoutImages = await _context.Products
                    .CountAsync(p => !p.Images.Any(), ct),
                PendingOlderThan24Hours = await _context.Orders
                    .CountAsync(o => o.Status == "Pending" &&
                                     o.CreatedAt < nowUtc.AddHours(-24), ct),

                // Revenue: COMPLETED orders only; not uncollected/unpaid orders.
                TotalRevenue = await _context.Orders.AsNoTracking()
                    .Where(o => o.Status == "Completed")
                    .SumAsync(o => (decimal?)o.TotalAmount, ct) ?? 0m,
                PeriodRevenue = completedCurrent.Sum(o => o.TotalAmount),
                PreviousPeriodRevenue = completedPrevious.Sum(o => o.TotalAmount),
                TodayRevenue = completedCurrent.Where(o => o.CreatedAt >= todayStartUtc)
                    .Sum(o => o.TotalAmount),
                TodayOrders = currentOrders.Count(o => o.CreatedAt >= todayStartUtc),
                PeriodOrders = currentOrders.Count,
                CompletedPeriodOrders = completedCurrent.Count,
                NewCustomers = await _context.Users.CountAsync(u => u.CreatedAt >= startUtc, ct)
            };

            // 7d = daily bars, 30d = 2-day bars, 90d = weekly bars.
            var bucketWidth = days == 7 ? 1 : days == 30 ? 2 : 7;
            for (var i = 0; i < days; i += bucketWidth)
            {
                var bucketStartLocal = startLocal.AddDays(i);
                var bucketEndLocal = startLocal.AddDays(Math.Min(days, i + bucketWidth));
                var bucketStartUtc = bucketStartLocal.Subtract(StoreOffset);
                var bucketEndUtc = bucketEndLocal.Subtract(StoreOffset);
                var label = bucketWidth == 1
                    ? bucketStartLocal.ToString("dd MMM")
                    : $"{bucketStartLocal:dd MMM}–{bucketEndLocal.AddDays(-1):dd MMM}";
                model.SalesTrend.Add(new DashboardSalesPoint
                {
                    Label = label,
                    Revenue = completedCurrent
                        .Where(o => o.CreatedAt >= bucketStartUtc && o.CreatedAt < bucketEndUtc)
                        .Sum(o => o.TotalAmount),
                    Orders = currentOrders.Count(o => o.CreatedAt >= bucketStartUtc &&
                                                       o.CreatedAt < bucketEndUtc)
                });
            }

            // Show every status actually present in the database; don't invent statuses.
            model.OrderStatuses = await _context.Orders.AsNoTracking()
                .GroupBy(o => o.Status)
                .Select(g => new DashboardOrderStatus
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(s => s.Count)
                .ToListAsync(ct);
            foreach (var status in model.OrderStatuses)
                status.SharePercent = model.TotalOrders == 0
                    ? 0 : Math.Round(100m * status.Count / model.TotalOrders, 1);

            model.StockAlerts = await _context.Products.AsNoTracking()
                .Where(p => p.IsPublished && p.StockQuantity <= 5)
                .OrderBy(p => p.StockQuantity)
                .ThenBy(p => p.Name)
                .Take(7)
                .Select(p => new DashboardStockAlert
                {
                    ProductId = p.Id,
                    Name = p.Name,
                    CategoryName = p.Category.Name,
                    StockQuantity = p.StockQuantity
                }).ToListAsync(ct);

            model.TopCategories = await _context.Products.AsNoTracking()
                .GroupBy(p => p.Category.Name)
                .Select(g => new DashboardCategoryOverview
                {
                    Name = g.Key,
                    ProductCount = g.Count()
                })
                .OrderByDescending(x => x.ProductCount)
                .Take(5).ToListAsync(ct);

            // Actual bestsellers: completed order items from the selected date range.
            model.TopSellers = await _context.OrderItems.AsNoTracking()
                .Where(oi => oi.Order.CreatedAt >= startUtc &&
                             oi.Order.CreatedAt <= nowUtc &&
                             oi.Order.Status == "Completed")
                .GroupBy(oi => new { oi.ProductVariant.ProductId, oi.ProductVariant.Product.Name })
                .Select(g => new DashboardProductSale
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.Name,
                    UnitsSold = g.Sum(oi => oi.Quantity),
                    SalesAmount = g.Sum(oi => oi.LineTotal)
                })
                .OrderByDescending(x => x.UnitsSold)
                .Take(5).ToListAsync(ct);

            model.RecentOrders = await _context.Orders.AsNoTracking()
                .OrderByDescending(o => o.CreatedAt)
                .Take(8).ToListAsync(ct);

            model.RecentCustomers = await _context.Users.AsNoTracking()
                .OrderByDescending(u => u.CreatedAt)
                .Take(5)
                .Select(u => new DashboardCustomer
                {
                    Name = u.FullName,
                    Email = u.Email ?? "",
                    CreatedAt = u.CreatedAt
                }).ToListAsync(ct);

            return View(model);
        }
    }
}
