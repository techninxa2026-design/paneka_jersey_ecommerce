using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public class VariantOptionItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? HexCode { get; set; }
        public int? DisplayOrder { get; set; }
        public bool? IsActive { get; set; }
        public int VariantCount { get; set; }
        public string SafeSwatch =>
            System.Text.RegularExpressions.Regex.IsMatch(HexCode ?? "", "^#[A-Fa-f0-9]{6}$")
                ? HexCode! : "#dadada";
    }

    public class VariantOptionsIndexViewModel
    {
        public string? Search { get; set; }
        public string Tab { get; set; } = "all";
        public List<VariantOptionItemViewModel> Sizes { get; set; } = new();
        public List<VariantOptionItemViewModel> Colors { get; set; } = new();
        public int TotalSizes { get; set; }
        public int TotalColors { get; set; }
        public int UsedSizes { get; set; }
        public int UsedColors { get; set; }
    }

    public class VariantOptionFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Please enter a name.")]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
        public string Name { get; set; } = "";

        // Supported when an equivalent property exists on the existing model.
        public string? HexCode { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;

        // View-only flags; always recomputed by the server.
        public bool IsColor { get; set; }
        public bool HasHexCode { get; set; }
        public bool HasDisplayOrder { get; set; }
        public bool HasIsActive { get; set; }
    }

    public class VariantOptionDeleteViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsColor { get; set; }
        public int VariantCount { get; set; }
    }
}
