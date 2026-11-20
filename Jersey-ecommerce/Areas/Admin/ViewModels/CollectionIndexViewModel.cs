using System.Collections.Generic;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public class CollectionIndexViewModel
    {
        public string? Search { get; set; }
        public string Status { get; set; } = "all";
        public int Total { get; set; }
        public int Active { get; set; }
        public int WithProducts { get; set; }
        public List<CollectionListItemViewModel> Items { get; set; } = new();
    }

    public class CollectionListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public int ProductCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
