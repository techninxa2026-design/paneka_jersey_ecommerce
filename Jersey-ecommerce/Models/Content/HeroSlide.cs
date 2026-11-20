
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Content
{
    public class HeroSlide
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Subtitle { get; set; }

        [StringLength(500)]
        public string? ImageUrl { get; set; }

        [StringLength(500)]
        public string? MobileImageUrl { get; set; }

        [StringLength(100)]
        public string? ButtonText { get; set; }

        [StringLength(500)]
        public string? ButtonUrl { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? StartsAt { get; set; }

        public DateTime? EndsAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
