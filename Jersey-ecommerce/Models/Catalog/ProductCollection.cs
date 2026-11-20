namespace Jersey_ecommerce.Models.Catalog
{
    public class ProductCollection
    {
        public int ProductId { get; set; }

        public int CollectionId { get; set; }

        public int DisplayOrder { get; set; }

        public Product Product { get; set; } = null!;

        public Collection Collection { get; set; } = null!;
    }
}
