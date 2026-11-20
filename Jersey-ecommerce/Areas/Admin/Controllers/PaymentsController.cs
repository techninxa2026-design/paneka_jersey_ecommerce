using System;
using System.Linq;
using System.Threading.Tasks;
using Jersey_ecommerce.Areas.Admin.ViewModels;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jersey_ecommerce.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const int PageSize = 15;

        public PaymentsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /admin/payments
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search, string? status, string? method,
            DateTime? from, DateTime? to, int page = 1)
        {
            search = search?.Trim();
            status = status?.Trim();
            method = method?.Trim();
            page = Math.Max(1, page);

            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
                (from, to) = (to, from);

            IQueryable<Payment> filtered = _context.Payments.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(p =>
                    p.Order.OrderNumber.Contains(search) ||
                    p.Order.CustomerName.Contains(search) ||
                    p.Order.CustomerEmail.Contains(search) ||
                    (p.TransactionReference != null && p.TransactionReference.Contains(search)) ||
                    (p.ProviderPaymentId != null && p.ProviderPaymentId.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(status))
                filtered = filtered.Where(p => p.Status == status);

            if (!string.IsNullOrWhiteSpace(method))
                filtered = filtered.Where(p => p.Method == method);

            if (from.HasValue)
                filtered = filtered.Where(p => p.CreatedAt >= from.Value.Date);

            if (to.HasValue && to.Value.Date < DateTime.MaxValue.Date)
            {
                var endExclusive = to.Value.Date.AddDays(1);
                filtered = filtered.Where(p => p.CreatedAt < endExclusive);
            }

            var baseQuery = _context.Payments.AsNoTracking();
            var filteredCount = await filtered.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling((double)filteredCount / PageSize));
            page = Math.Min(page, totalPages);

            var rows = await filtered
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(p => new PaymentAdminRowViewModel
                {
                    Id = p.Id,
                    OrderId = p.OrderId,
                    OrderNumber = p.Order.OrderNumber,
                    CustomerName = p.Order.CustomerName,
                    CustomerEmail = p.Order.CustomerEmail,
                    Method = p.Method,
                    Status = p.Status,
                    Provider = p.Provider,
                    TransactionReference = p.TransactionReference,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    CreatedAt = p.CreatedAt,
                    PaidAt = p.PaidAt,
                    TransactionCount = p.Transactions.Count
                })
                .ToListAsync();

            var model = new PaymentsIndexViewModel
            {
                Rows = rows,
                Search = search,
                Status = status,
                Method = method,
                From = from,
                To = to,
                Page = page,
                PageSize = PageSize,
                FilteredCount = filteredCount,
                TotalCount = await baseQuery.CountAsync(),
                PendingCount = await baseQuery.CountAsync(p => p.Status == "Pending"),
                // This is the DATABASE-RECORDED status, not independent gateway reconciliation.
                ReportedPaidCount = await baseQuery.CountAsync(p =>
                    p.Status == "Paid" || p.Status == "Succeeded" || p.Status == "Completed"),
                FailedCount = await baseQuery.CountAsync(p => p.Status == "Failed"),
                RecordedAmount = await baseQuery.SumAsync(p => (decimal?)p.Amount) ?? 0m,
                StatusOptions = await baseQuery.Select(p => p.Status)
                    .Distinct().OrderBy(s => s).ToListAsync(),
                MethodOptions = await baseQuery.Select(p => p.Method)
                    .Distinct().OrderBy(m => m).ToListAsync()
            };
            return View(model);
        }

        // GET /admin/payments/details/123
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var payment = await _context.Payments
                .AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.Order)
                .Include(p => p.Transactions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null)
                return NotFound();

            return View(new PaymentDetailsViewModel { Payment = payment });
        }

        // GET /admin/payments/transactions
        [HttpGet]
        public async Task<IActionResult> Transactions(
            string? search, string? status, string? type, int page = 1)
        {
            search = search?.Trim();
            status = status?.Trim();
            type = type?.Trim();
            page = Math.Max(1, page);

            IQueryable<PaymentTransaction> filtered =
                _context.PaymentTransactions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(t =>
                    t.Payment.Order.OrderNumber.Contains(search) ||
                    (t.Reference != null && t.Reference.Contains(search)) ||
                    (t.ProviderTransactionId != null && t.ProviderTransactionId.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(status))
                filtered = filtered.Where(t => t.Status == status);

            if (!string.IsNullOrWhiteSpace(type))
                filtered = filtered.Where(t => t.TransactionType == type);

            var baseQuery = _context.PaymentTransactions.AsNoTracking();
            var count = await filtered.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling((double)count / PageSize));
            page = Math.Min(page, totalPages);

            var rows = await filtered
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(t => new PaymentTransactionRowViewModel
                {
                    Id = t.Id,
                    PaymentId = t.PaymentId,
                    OrderId = t.Payment.OrderId,
                    OrderNumber = t.Payment.Order.OrderNumber,
                    TransactionType = t.TransactionType,
                    Status = t.Status,
                    ProviderTransactionId = t.ProviderTransactionId,
                    Reference = t.Reference,
                    Amount = t.Amount,
                    Currency = t.Payment.Currency,
                    CreatedAt = t.CreatedAt,
                    CompletedAt = t.CompletedAt,
                    FailureReason = t.FailureReason
                })
                .ToListAsync();

            return View(new PaymentTransactionsIndexViewModel
            {
                Rows = rows,
                Search = search,
                Status = status,
                Type = type,
                Page = page,
                PageSize = PageSize,
                FilteredCount = count,
                TotalCount = await baseQuery.CountAsync(),
                StatusOptions = await baseQuery.Select(t => t.Status)
                    .Distinct().OrderBy(x => x).ToListAsync(),
                TypeOptions = await baseQuery.Select(t => t.TransactionType)
                    .Distinct().OrderBy(x => x).ToListAsync()
            });
        }
    }
}
