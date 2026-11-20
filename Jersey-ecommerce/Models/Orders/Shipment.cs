
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Orders
{
    public class Shipment
    {
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        [StringLength(100)]
        public string? CourierName { get; set; }

        [StringLength(200)]
        public string? TrackingNumber { get; set; }

        [StringLength(500)]
        public string? TrackingUrl { get; set; }

        public DateTime? ShippedAt { get; set; }

        public DateTime? EstimatedDeliveryAt { get; set; }

        public DateTime? DeliveredAt { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Order Order { get; set; } = null!;
    }
}
