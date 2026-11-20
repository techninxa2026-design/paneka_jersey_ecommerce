using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    // Replace any older CustomerViewModels.cs file with this one; do not keep duplicate class definitions.
    public sealed class CustomerFormViewModel
    {
        public string? Id { get; set; }
        public bool IsEdit => !string.IsNullOrWhiteSpace(Id);

        [Required(ErrorMessage = "Customer name is required.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Enter a name between 2 and 150 characters.")]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256)]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [StringLength(40)]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        // Optional on Edit. Controller enforces a password when creating.
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        public string? ConfirmPassword { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public sealed class CustomersIndexViewModel
    {
        public string? Search { get; set; }
        public string Status { get; set; } = "all";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 12;
        public int TotalCustomers { get; set; }
        public int ActiveCustomers { get; set; }
        public int InactiveCustomers { get; set; }
        public int TotalFiltered { get; set; }
        public int TotalPages => Math.Max(1, (TotalFiltered + Math.Max(1, PageSize) - 1) / Math.Max(1, PageSize));
        public List<CustomerRowViewModel> Customers { get; set; } = new();
    }

    public sealed class CustomerRowViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int OrderCount { get; set; }
        public decimal TotalSpent { get; set; }
    }

    public sealed class CustomerDetailsViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public bool EmailConfirmed { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalOrders { get; set; }
        public decimal TotalSpent { get; set; }
        public List<CustomerAddressViewModel> Addresses { get; set; } = new();
        public List<CustomerOrderViewModel> RecentOrders { get; set; } = new();
    }

    public sealed class CustomerAddressViewModel
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? District { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public bool IsDefault { get; set; }
    }

    public sealed class CustomerOrderViewModel
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
