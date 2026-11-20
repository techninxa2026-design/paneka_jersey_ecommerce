using System;
using System.Collections.Generic;
using Jersey_ecommerce.Models.Orders;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public sealed class PaymentAdminRowViewModel
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Provider { get; set; }
        public string? TransactionReference { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public int TransactionCount { get; set; }
    }

    public sealed class PaymentsIndexViewModel
    {
        public List<PaymentAdminRowViewModel> Rows { get; set; } = new();
        public List<string> StatusOptions { get; set; } = new();
        public List<string> MethodOptions { get; set; } = new();
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string? Method { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public int FilteredCount { get; set; }
        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int ReportedPaidCount { get; set; }
        public int FailedCount { get; set; }
        public decimal RecordedAmount { get; set; }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)FilteredCount / PageSize));
    }

    public sealed class PaymentDetailsViewModel
    {
        public Payment Payment { get; set; } = null!;
    }

    public sealed class PaymentTransactionRowViewModel
    {
        public int Id { get; set; }
        public int PaymentId { get; set; }
        public int OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ProviderTransactionId { get; set; }
        public string? Reference { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? FailureReason { get; set; }
    }

    public sealed class PaymentTransactionsIndexViewModel
    {
        public List<PaymentTransactionRowViewModel> Rows { get; set; } = new();
        public List<string> TypeOptions { get; set; } = new();
        public List<string> StatusOptions { get; set; } = new();
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string? Type { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public int FilteredCount { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)FilteredCount / PageSize));
    }
}
