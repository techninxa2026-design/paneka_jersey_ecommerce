
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Content
{
    public class NewsletterSubscriber
    {
        public int Id { get; set; }

        [Required]
        [StringLength(256)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public bool IsSubscribed { get; set; } = true;

        public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UnsubscribedAt { get; set; }
    }
}
