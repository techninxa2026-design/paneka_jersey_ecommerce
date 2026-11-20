
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public class CategoryFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Category name is required.")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(120)]
        public string? Slug { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [Range(0, int.MaxValue,
            ErrorMessage = "Display order cannot be negative.")]
        public int DisplayOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public string? ImageUrl { get; set; }

        public IFormFile? ImageFile { get; set; }
    }
}
