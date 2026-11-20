
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Content
{
    public class Banner
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Subtitle { get; set; }

        [Required]
        [StringLength(500)]
        public string ImageUrl { get; set; } = string.Empty;

        [StringLength(500)]
        public string? LinkUrl { get; set; }

        [StringLength(50)]
        public string Placement { get; set; } = "Home";

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? StartsAt { get; set; }

        public DateTime? EndsAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
