
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Areas.Identity;

namespace Jersey_ecommerce.Models.Customers
{
    public class Address
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string AddressLine1 { get; set; } = string.Empty;

        [StringLength(200)]
        public string? AddressLine2 { get; set; }

        [Required]
        [StringLength(100)]
        public string City { get; set; } = string.Empty;

        [StringLength(100)]
        public string? District { get; set; }

        [StringLength(20)]
        public string? PostalCode { get; set; }

        [Required]
        [StringLength(100)]
        public string Country { get; set; } = "Bangladesh";

        public bool IsDefault { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser User { get; set; } = null!;
    }
}
