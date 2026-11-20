using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.ViewModels.Account
{
    public class ProfileViewModel
    {
        [Required]
        [StringLength(150, MinimumLength = 2)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        public string? CurrentImageUrl { get; set; }

        [Display(Name = "Change profile picture")]
        public IFormFile? ProfileImage { get; set; }
    }
}
