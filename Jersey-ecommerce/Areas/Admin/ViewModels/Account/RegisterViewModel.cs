using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.ViewModels.Account
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Enter your full name.")]
        [StringLength(150, MinimumLength = 2)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Phone number (optional)")]
        public string? PhoneNumber { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must contain at least 8 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "Profile picture (optional)")]
        public IFormFile? ProfileImage { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
