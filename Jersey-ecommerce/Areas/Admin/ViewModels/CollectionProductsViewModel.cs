using System.Collections.Generic;

namespace Jersey_ecommerce.Areas.Admin.ViewModels
{
    public class CollectionProductsViewModel
    {
        public int CollectionId { get; set; }
        public string CollectionName { get; set; } = string.Empty;
        public string? Search { get; set; }
        public List<int> SelectedProductIds { get; set; } = new();
        public List<CollectionProductOptionViewModel> Products { get; set; } = new();
    }

    public class CollectionProductOptionViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public bool Selected { get; set; }
    }
}
