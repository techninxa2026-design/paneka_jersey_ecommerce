using System;
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Models.Catalog;

namespace Jersey_ecommerce.Models.Customers
{
    // If this class already exists in your project, MERGE missing properties,
    // preserving existing columns and data instead of blindly replacing it.
    public class Review
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        [Required]
        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;

        [Range(1, 5)]
        public int Rating { get; set; }

        [MaxLength(180)]
        public string? Title { get; set; }

        [Required, MaxLength(2000)]
        public string Comment { get; set; } = string.Empty;

        public bool IsApproved { get; set; } = false;
        public bool IsRejected { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
