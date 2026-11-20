using System.ComponentModel.DataAnnotations;

namespace Jersey_ecommerce.Models.Catalog
{
    public class Size
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string Name { get; set; } = string.Empty;

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<ProductVariant> Variants { get; set; }
            = new List<ProductVariant>();
    }
}
