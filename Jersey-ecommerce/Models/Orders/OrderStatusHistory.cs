
using System.ComponentModel.DataAnnotations;
using Jersey_ecommerce.Areas.Identity;

namespace Jersey_ecommerce.Models.Orders
{
    public class OrderStatusHistory
    {
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Note { get; set; }

        public string? ChangedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Order Order { get; set; } = null!;

        public ApplicationUser? ChangedByUser { get; set; }
    }
}
