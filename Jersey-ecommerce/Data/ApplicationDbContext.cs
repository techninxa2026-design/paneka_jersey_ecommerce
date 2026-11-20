
using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Customers;
using Jersey_ecommerce.Models.Shopping;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Models.Promotions;
using Jersey_ecommerce.Models.Content;
using Jersey_ecommerce.Models.Customers;
using Jersey_ecommerce.Models.Checkout;

namespace Jersey_ecommerce.Data
{
    public class ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : IdentityDbContext<ApplicationUser>(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<BkashPaymentAttempt>(entity =>
            {
                entity.HasIndex(x => x.GatewayPaymentId).IsUnique();
                entity.HasIndex(x => new { x.OrderId, x.CreatedAt });
                entity.HasIndex(x => x.Nonce).IsUnique();
                entity.HasOne<Jersey_ecommerce.Models.Orders.Order>()
                    .WithMany()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            // ==========================================
            // CUSTOMERS: APPLICATION USER -> ADDRESSES
            // ==========================================
            builder.Entity<Address>()
                .HasOne(a => a.User)
                .WithMany(u => u.Addresses)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================================
            // CATALOG: CATEGORY -> PRODUCTS
            // ==========================================
            builder.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // CATALOG: BRAND -> PRODUCTS
            // ==========================================
            builder.Entity<Product>()
                .HasOne(p => p.Brand)
                .WithMany(b => b.Products)
                .HasForeignKey(p => p.BrandId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // CATALOG: PRODUCT -> IMAGES
            // ==========================================
            builder.Entity<ProductImage>()
                .HasOne(i => i.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================================
            // CATALOG: PRODUCT -> VARIANTS
            // ==========================================
            builder.Entity<ProductVariant>()
                .HasOne(v => v.Product)
                .WithMany(p => p.Variants)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // CATALOG: SIZE -> VARIANTS
            // ==========================================
            builder.Entity<ProductVariant>()
                .HasOne(v => v.Size)
                .WithMany(s => s.Variants)
                .HasForeignKey(v => v.SizeId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // CATALOG: COLOR -> VARIANTS
            // ==========================================
            builder.Entity<ProductVariant>()
                .HasOne(v => v.Color)
                .WithMany(c => c.Variants)
                .HasForeignKey(v => v.ColorId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // CATALOG: PRODUCT VARIANT INDEXES
            // ==========================================
            builder.Entity<ProductVariant>()
                .HasIndex(v => v.SKU)
                .IsUnique();

            builder.Entity<ProductVariant>()
                .HasIndex(v => new
                {
                    v.ProductId,
                    v.SizeId,
                    v.ColorId
                })
                .IsUnique();

            // ==========================================
            // CATALOG: PRODUCT <-> COLLECTIONS
            // ==========================================
            builder.Entity<ProductCollection>()
                .HasKey(pc => new
                {
                    pc.ProductId,
                    pc.CollectionId
                });

            builder.Entity<ProductCollection>()
                .HasOne(pc => pc.Product)
                .WithMany(p => p.ProductCollections)
                .HasForeignKey(pc => pc.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ProductCollection>()
                .HasOne(pc => pc.Collection)
                .WithMany(c => c.ProductCollections)
                .HasForeignKey(pc => pc.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================================
            // CATALOG: UNIQUE SLUGS
            // ==========================================
            builder.Entity<Category>()
                .HasIndex(c => c.Slug)
                .IsUnique();

            builder.Entity<Brand>()
                .HasIndex(b => b.Slug)
                .IsUnique();

            builder.Entity<Product>()
                .HasIndex(p => p.Slug)
                .IsUnique();

            builder.Entity<Collection>()
                .HasIndex(c => c.Slug)
                .IsUnique();

            // ==========================================
            // CATALOG: MONEY PRECISION
            // ==========================================
            builder.Entity<Product>()
                .Property(p => p.BasePrice)
                .HasPrecision(18, 2);

            builder.Entity<Product>()
                .Property(p => p.CompareAtPrice)
                .HasPrecision(18, 2);

            builder.Entity<ProductVariant>()
                .Property(v => v.Price)
                .HasPrecision(18, 2);

            // ==========================================
            // CATALOG: CHECK CONSTRAINTS
            // ==========================================
            builder.Entity<ProductVariant>()
                .ToTable(table =>
                {
                    table.HasCheckConstraint(
                        "CK_ProductVariant_StockQuantity",
                        "[StockQuantity] >= 0");

                    table.HasCheckConstraint(
                        "CK_ProductVariant_Price",
                        "[Price] >= 0");
                });

            builder.Entity<Product>()
                .ToTable(table =>
                {
                    table.HasCheckConstraint(
                        "CK_Product_BasePrice",
                        "[BasePrice] >= 0");
                });

            // ==========================================
            // SHOPPING: USER -> SHOPPING CART
            // One cart per user
            // ==========================================
            builder.Entity<ShoppingCart>()
                .HasOne(c => c.User)
                .WithOne(u => u.ShoppingCart)
                .HasForeignKey<ShoppingCart>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ShoppingCart>()
                .HasIndex(c => c.UserId)
                .IsUnique();

            // ==========================================
            // SHOPPING: CART -> CART ITEMS
            // ==========================================
            builder.Entity<CartItem>()
                .HasOne(ci => ci.ShoppingCart)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.ShoppingCartId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<CartItem>()
                .HasOne(ci => ci.ProductVariant)
                .WithMany()
                .HasForeignKey(ci => ci.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CartItem>()
                .HasIndex(ci => new
                {
                    ci.ShoppingCartId,
                    ci.ProductVariantId
                })
                .IsUnique();

            // ==========================================
            // SHOPPING: USER -> WISHLIST
            // One wishlist per user
            // ==========================================
            builder.Entity<WishList>()
                .HasOne(w => w.User)
                .WithOne(u => u.Wishlist)
                .HasForeignKey<WishList>(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WishList>()
                .HasIndex(w => w.UserId)
                .IsUnique();

            // ==========================================
            // SHOPPING: WISHLIST -> WISHLIST ITEMS
            // ==========================================
            // This uses WishList and WishListId, matching the
            // WishListItem model from the earlier code.
            builder.Entity<WishListItem>()
                .HasOne(wi => wi.Wishlist)
                .WithMany(w => w.Items)
                .HasForeignKey(wi => wi.WishlistId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WishListItem>()
                .HasOne(wi => wi.Product)
                .WithMany()
                .HasForeignKey(wi => wi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<WishListItem>()
                .HasIndex(wi => new
                {
                    wi.WishlistId,
                    wi.ProductId
                })
                .IsUnique();

            // ==========================================
            // CUSTOMERS: USER -> REVIEWS
            // ==========================================
            builder.Entity<Review>()
                .HasOne(r => r.User)
                .WithMany(u => u.Reviews)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Review>()
                .HasOne(r => r.Product)
                .WithMany()
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Review>()
                .HasIndex(r => new
                {
                    r.UserId,
                    r.ProductId
                })
                .IsUnique();

            // ==========================================
            // ORDERS: USER -> ORDERS
            // ==========================================
            builder.Entity<Order>()
                .HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Order>()
                .HasIndex(o => o.OrderNumber)
                .IsUnique();

            // ==========================================
            // ORDERS: ORDER -> ORDER ITEMS
            // ==========================================
            builder.Entity<OrderItem>()
                .HasOne(oi => oi.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<OrderItem>()
                .HasOne(oi => oi.ProductVariant)
                .WithMany()
                .HasForeignKey(oi => oi.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // ORDERS: ORDER -> PAYMENT
            // One payment record per order
            // ==========================================
            builder.Entity<Payment>()
                .HasOne(p => p.Order)
                .WithOne(o => o.Payment)
                .HasForeignKey<Payment>(p => p.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Payment>()
                .HasIndex(p => p.OrderId)
                .IsUnique();

            // A reference may be customer-reported, but it must never be
            // accepted against two different bKash orders. A database-enforced
            // filtered index also protects against simultaneous submissions.
            builder.Entity<Payment>()
                .HasIndex(p => p.TransactionReference)
                .IsUnique()
                .HasDatabaseName("IX_Payments_ManualBkashTrxId")
                .HasFilter("[Method] = 'Bkash' AND [TransactionReference] IS NOT NULL");

            // ==========================================
            // PAYMENTS: PAYMENT -> TRANSACTIONS
            // ==========================================
            builder.Entity<PaymentTransaction>()
                .HasOne(pt => pt.Payment)
                .WithMany(p => p.Transactions)
                .HasForeignKey(pt => pt.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================================
            // SHIPPING: ORDER -> SHIPMENT
            // One shipment record per order
            // ==========================================
            builder.Entity<Shipment>()
                .HasOne(s => s.Order)
                .WithOne(o => o.Shipment)
                .HasForeignKey<Shipment>(s => s.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Shipment>()
                .HasIndex(s => s.OrderId)
                .IsUnique();

            // ==========================================
            // ORDERS: ORDER -> STATUS HISTORY
            // ==========================================
            builder.Entity<OrderStatusHistory>()
                .HasOne(h => h.Order)
                .WithMany(o => o.StatusHistory)
                .HasForeignKey(h => h.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<OrderStatusHistory>()
                .HasOne(h => h.ChangedByUser)
                .WithMany()
                .HasForeignKey(h => h.ChangedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // ==========================================
            // INVENTORY: VARIANT -> INVENTORY TRANSACTIONS
            // ==========================================
            builder.Entity<InventoryTransaction>()
                .HasOne(it => it.ProductVariant)
                .WithMany()
                .HasForeignKey(it => it.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<InventoryTransaction>()
                .HasOne(it => it.Order)
                .WithMany()
                .HasForeignKey(it => it.OrderId)
                .OnDelete(DeleteBehavior.SetNull);

            // ==========================================
            // PROMOTIONS: COUPONS
            // ==========================================
            builder.Entity<Coupon>()
                .HasIndex(c => c.Code)
                .IsUnique();

            builder.Entity<Coupon>()
                .Property(c => c.DiscountValue)
                .HasPrecision(18, 2);

            builder.Entity<Coupon>()
                .Property(c => c.MinimumOrderAmount)
                .HasPrecision(18, 2);

            builder.Entity<Coupon>()
                .Property(c => c.MaximumDiscountAmount)
                .HasPrecision(18, 2);

            // ==========================================
            // PROMOTIONS: COUPON USAGE
            // ==========================================
            builder.Entity<CouponUsage>()
                .HasOne(cu => cu.Coupon)
                .WithMany(c => c.Usages)
                .HasForeignKey(cu => cu.CouponId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CouponUsage>()
                .HasOne(cu => cu.User)
                .WithMany()
                .HasForeignKey(cu => cu.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CouponUsage>()
                .HasOne(cu => cu.Order)
                .WithMany()
                .HasForeignKey(cu => cu.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CouponUsage>()
                .HasIndex(cu => cu.OrderId)
                .IsUnique();

            builder.Entity<CouponUsage>()
                .Property(cu => cu.DiscountAmount)
                .HasPrecision(18, 2);

            // ==========================================
            // MONEY PRECISION: ORDERS
            // ==========================================
            builder.Entity<Order>()
                .Property(o => o.Subtotal)
                .HasPrecision(18, 2);

            builder.Entity<Order>()
                .Property(o => o.DiscountAmount)
                .HasPrecision(18, 2);

            builder.Entity<Order>()
                .Property(o => o.ShippingCost)
                .HasPrecision(18, 2);

            builder.Entity<Order>()
                .Property(o => o.TaxAmount)
                .HasPrecision(18, 2);

            builder.Entity<Order>()
                .Property(o => o.TotalAmount)
                .HasPrecision(18, 2);

            // ==========================================
            // MONEY PRECISION: ORDER ITEMS
            // ==========================================
            builder.Entity<OrderItem>()
                .Property(oi => oi.UnitPrice)
                .HasPrecision(18, 2);

            builder.Entity<OrderItem>()
                .Property(oi => oi.DiscountAmount)
                .HasPrecision(18, 2);

            builder.Entity<OrderItem>()
                .Property(oi => oi.LineTotal)
                .HasPrecision(18, 2);

            // ==========================================
            // MONEY PRECISION: PAYMENTS
            // ==========================================
            builder.Entity<Payment>()
                .Property(p => p.Amount)
                .HasPrecision(18, 2);

            builder.Entity<PaymentTransaction>()
                .Property(pt => pt.Amount)
                .HasPrecision(18, 2);

            // ==========================================
            // CONTENT: SITE SETTINGS
            // ==========================================
            // ==========================================
            // CONTENT: SITE SETTINGS
            // ==========================================
            builder.Entity<SiteSettings>()
                .HasIndex(s => s.Key)
                .IsUnique();
            // ==========================================
            // CONTENT: NEWSLETTER SUBSCRIBERS
            // ==========================================
            builder.Entity<NewsletterSubscriber>()
                .HasIndex(n => n.Email)
                .IsUnique();
        }

        // ==========================================
        // IDENTITY TABLES
        // Inherited from IdentityDbContext<ApplicationUser>
        // ==========================================

        // ==========================================
        // SHOPPING
        // ==========================================
        // ==========================================
        // ORDERS
        // ==========================================
        public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
        public DbSet<Order> Orders => Set<Order>();

        public DbSet<OrderItem> OrderItems => Set<OrderItem>();

        public DbSet<Payment> Payments => Set<Payment>();

        public DbSet<PaymentTransaction> PaymentTransactions
            => Set<PaymentTransaction>();

        public DbSet<Shipment> Shipments => Set<Shipment>();
        public DbSet<BkashPaymentAttempt> BkashPaymentAttempts => Set<BkashPaymentAttempt>();

        public DbSet<OrderStatusHistory> OrderStatusHistories
            => Set<OrderStatusHistory>();

        public DbSet<InventoryTransaction> InventoryTransactions
            => Set<InventoryTransaction>();

        // ==========================================
        // PROMOTIONS
        // ==========================================
        public DbSet<Coupon> Coupons => Set<Coupon>();

        public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();

        // ==========================================
        // CONTENT MANAGEMENT
        // ==========================================
        public DbSet<HeroSlide> HeroSlides => Set<HeroSlide>();

        public DbSet<Banner> Banners => Set<Banner>();

       

        public DbSet<NewsletterSubscriber> NewsletterSubscribers
            => Set<NewsletterSubscriber>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<Address> Addresses => Set<Address>();
        public DbSet<ShoppingCart> ShoppingCarts
            => Set<ShoppingCart>();

        public DbSet<CartItem> CartItems
            => Set<CartItem>();

        public DbSet<WishList> WishLists
            => Set<WishList>();

        public DbSet<WishListItem> WishListItems
            => Set<WishListItem>();

        // ==========================================
        // CATALOG
        // ==========================================
        public DbSet<Category> Categories
            => Set<Category>();

        public DbSet<Brand> Brands
            => Set<Brand>();

        public DbSet<Product> Products
            => Set<Product>();

        public DbSet<ProductImage> ProductImages
            => Set<ProductImage>();

        public DbSet<Size> Sizes
            => Set<Size>();

        public DbSet<Color> Colors
            => Set<Color>();

        public DbSet<ProductVariant> ProductVariants
            => Set<ProductVariant>();
        public DbSet<ProductVariantImage> ProductVariantImages
       => Set<ProductVariantImage>();
        public DbSet<Collection> Collections
            => Set<Collection>();

        public DbSet<ProductCollection> ProductCollections
            => Set<ProductCollection>();
       
    }
}
