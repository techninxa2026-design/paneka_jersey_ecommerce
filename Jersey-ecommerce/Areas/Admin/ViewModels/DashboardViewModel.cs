using System;
using System.Collections.Generic;
using Jersey_ecommerce.Models.Orders;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public sealed class DashboardViewModel
    {
        // Time range uses Bangladesh-local calendar days (UTC+06:00).
        public int Days { get; set; } = 30;
        public DateTime LocalNow { get; set; }
        public DateTime RangeStartLocal { get; set; }
        public DateTime RangeEndLocal { get; set; }

        // Lifetime totals
        public int TotalProducts { get; set; }
        public int TotalCategories { get; set; }
        public int TotalBrands { get; set; }
        public int TotalOrders { get; set; }
        public int PendingOrders { get; set; }
        public int TotalCustomers { get; set; }
        public decimal TotalRevenue { get; set; } // Completed orders only.
        public int TotalCoupons { get; set; }
        public int TotalReviews { get; set; }
        public int TotalShipments { get; set; }

        // Catalog / shared Product.StockQuantity inventory
        public int PublishedProducts { get; set; }
        public int DraftProducts { get; set; }
        public int FeaturedProducts { get; set; }
        public long AvailableStockUnits { get; set; }
        public int LowStockProducts { get; set; }
        public int OutOfStockProducts { get; set; }
        public int ProductsWithoutImages { get; set; }
        public int PendingOlderThan24Hours { get; set; }
        // Historical name preserved for compatibility with any existing references.
        public int LowStockVariants => LowStockProducts;

        // Period KPIs
        public decimal PeriodRevenue { get; set; }
        public decimal PreviousPeriodRevenue { get; set; }
        public decimal TodayRevenue { get; set; }
        public int TodayOrders { get; set; }
        public int PeriodOrders { get; set; }
        public int CompletedPeriodOrders { get; set; }
        public int NewCustomers { get; set; }
        public decimal AverageOrderValue => CompletedPeriodOrders == 0
            ? 0m : PeriodRevenue / CompletedPeriodOrders;
        public decimal? RevenueChangePercent => PreviousPeriodRevenue == 0m
            ? null
            : ((PeriodRevenue - PreviousPeriodRevenue) / PreviousPeriodRevenue) * 100m;

        public List<DashboardSalesPoint> SalesTrend { get; set; } = new();
        public List<DashboardOrderStatus> OrderStatuses { get; set; } = new();
        public List<DashboardStockAlert> StockAlerts { get; set; } = new();
        public List<DashboardProductSale> TopSellers { get; set; } = new();
        public List<DashboardCategoryOverview> TopCategories { get; set; } = new();
        public List<DashboardCustomer> RecentCustomers { get; set; } = new();
        public List<Order> RecentOrders { get; set; } = new();
    }

    public sealed class DashboardSalesPoint
    {
        public string Label { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public int Orders { get; set; }
    }

    public sealed class DashboardOrderStatus
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal SharePercent { get; set; }
    }

    public sealed class DashboardStockAlert
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
    }

    public sealed class DashboardProductSale
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int UnitsSold { get; set; }
        public decimal SalesAmount { get; set; }
    }

    public sealed class DashboardCategoryOverview
    {
        public string Name { get; set; } = string.Empty;
        public int ProductCount { get; set; }
    }

    public sealed class DashboardCustomer
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
