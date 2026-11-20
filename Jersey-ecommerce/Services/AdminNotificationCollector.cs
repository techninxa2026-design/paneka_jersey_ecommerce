using Jersey_ecommerce.Areas.Identity;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Content;
using Jersey_ecommerce.Models.Customers;
using Jersey_ecommerce.Models.Notifications;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Models.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Jersey_ecommerce.Services;

/// <summary>
/// One central domain-event adapter for all writes through ApplicationDbContext.
/// The pre-save snapshot captures what actually changed; each deferred builder
/// gets the generated record ID after the business save has succeeded.
/// This avoids scattering fragile notification calls across controllers.
/// </summary>
public static class AdminNotificationCollector
{
    public static List<Func<AdminNotification>> Collect(ApplicationDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var drafts = new List<Func<AdminNotification>>();

        void Add(string category, string code, string severity,
            Func<string> title, Func<string> message, Func<string> url)
            => drafts.Add(() => new AdminNotification
            {
                Category = category,
                EventType = code,
                Severity = severity,
                Title = Clip(title(), 160),
                Message = Clip(message(), 500),
                TargetUrl = url(),
                CreatedAt = DateTime.UtcNow
            });

        var paymentPaid = db.ChangeTracker.Entries<Payment>()
            .Any(e => Changed(e, nameof(Payment.Status)) && e.Entity.Status == "Paid");

        foreach (var e in db.ChangeTracker.Entries().ToArray())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            switch (e.Entity)
            {
                // Removal events link to the containing module, never to an
                // ID that has just been deleted.
                case Order order when e.State == EntityState.Deleted:
                    Add("Orders", "order.deleted", "Critical",
                        () => "Order record deleted",
                        () => $"Order {order.OrderNumber} was removed from the database.",
                        () => "/Admin/Orders");
                    break;
                case Payment payment when e.State == EntityState.Deleted:
                    Add("Payments", "payment.deleted", "Critical",
                        () => "Payment record removed",
                        () => $"Payment record for order #{payment.OrderId} was deleted.",
                        () => "/Admin/Payments");
                    break;
                case Shipment shipment when e.State == EntityState.Deleted:
                    Add("Shipping", "shipment.deleted", "Warning",
                        () => "Shipment record removed",
                        () => $"Shipment record for order #{shipment.OrderId} was deleted.",
                        () => "/Admin/Shipments");
                    break;
                case Review review when e.State == EntityState.Deleted:
                    Add("Reviews", "review.deleted", "Warning",
                        () => "Product review deleted",
                        () => $"A {review.Rating}-star review was removed by staff.",
                        () => "/Admin/Reviews");
                    break;
                case Product product when e.State == EntityState.Deleted:
                    Add("Catalog", "product.deleted", "Warning",
                        () => "Product deleted",
                        () => $"Product '{product.Name}' was removed.",
                        () => "/Admin/Products");
                    break;
                case ProductVariant variant when e.State == EntityState.Deleted:
                    Add("Inventory", "variant.deleted", "Warning",
                        () => "Product variant removed",
                        () => $"SKU {variant.SKU} was removed from the store catalog.",
                        () => "/Admin/Products");
                    break;
                case ApplicationUser customer when e.State == EntityState.Deleted:
                    Add("Customers", "customer.deleted", "Warning",
                        () => "Customer account deleted",
                        () => $"Customer account {customer.Email ?? customer.UserName ?? "unknown"} was removed.",
                        () => "/Admin/Customers");
                    break;
                case Coupon coupon when e.State == EntityState.Deleted:
                    Add("Promotions", "coupon.deleted", "Warning",
                        () => "Coupon deleted",
                        () => $"Coupon '{coupon.Code}' was removed.",
                        () => "/Admin/Coupons");
                    break;
                case Brand brand when e.State == EntityState.Deleted:
                    Add("Catalog", "brand.deleted", "Info",
                        () => "Brand removed",
                        () => $"Brand '{brand.Name}' was deleted.",
                        () => "/Admin/Brands");
                    break;
                case Category category when e.State == EntityState.Deleted:
                    Add("Catalog", "category.deleted", "Info",
                        () => "Category removed",
                        () => $"Category '{category.Name}' was deleted.",
                        () => "/Admin/Categories");
                    break;
                case Collection collection when e.State == EntityState.Deleted:
                    Add("Catalog", "collection.deleted", "Info",
                        () => "Collection removed",
                        () => $"Collection '{collection.Name}' was deleted.",
                        () => "/Admin/Collections");
                    break;
                case Banner banner when e.State == EntityState.Deleted:
                    Add("Content", "banner.deleted", "Info",
                        () => "Banner removed",
                        () => $"Banner '{banner.Title}' was deleted.",
                        () => "/Admin/Banners");
                    break;
                case HeroSlide slide when e.State == EntityState.Deleted:
                    Add("Content", "slide.deleted", "Info",
                        () => "Homepage slide removed",
                        () => $"Slide '{slide.Title}' was deleted.",
                        () => "/Admin/Banners");
                    break;
                case Order order when e.State == EntityState.Added:
                    Add("Orders", "order.created", "Info",
                        () => $"New order {order.OrderNumber}",
                        () => $"A customer placed order {order.OrderNumber}. Review payment and fulfillment.",
                        () => $"/Admin/Orders/Details/{order.Id}");
                    break;
                case Order order when Changed(e, nameof(Order.Status)) && !paymentPaid:
                    var newOrderStatus = order.Status;
                    Add("Orders", "order.status", newOrderStatus is "Cancelled" or "Returned" ? "Warning" : "Info",
                        () => $"Order {order.OrderNumber}: {newOrderStatus}",
                        () => $"Order {order.OrderNumber} moved to {newOrderStatus}.",
                        () => $"/Admin/Orders/Details/{order.Id}");
                    break;
                case Payment payment when Changed(e, nameof(Payment.Status)):
                    var newPaymentStatus = payment.Status;
                    var severity = newPaymentStatus switch
                    {
                        "Submitted" or "Rejected" or "Failed" => "Warning",
                        "Paid" => "Success",
                        _ => "Info"
                    };
                    Add("Payments", $"payment.{newPaymentStatus.ToLowerInvariant()}", severity,
                        () => newPaymentStatus switch
                        {
                            "Submitted" => "bKash payment needs verification",
                            "Paid" => "Payment verified — order confirmed",
                            "Rejected" => "bKash payment claim rejected",
                            _ => $"Payment status: {newPaymentStatus}"
                        },
                        () => $"Payment for order #{payment.OrderId} changed to {newPaymentStatus}. Review the payment and order record.",
                        () => $"/Admin/Orders/Details/{payment.OrderId}#payment-review");
                    break;
                case Shipment shipment when Changed(e, nameof(Shipment.Status)):
                    var status = shipment.Status;
                    Add("Shipping", $"shipment.{status.ToLowerInvariant()}", status is "Failed" or "Returned" ? "Warning" : "Info",
                        () => $"Shipment {status.ToLowerInvariant()}",
                        () => $"Shipment for order #{shipment.OrderId} is now {status}.",
                        () => $"/Admin/Shipments/Details/{shipment.Id}");
                    break;
                case Shipment shipment when Changed(e, nameof(Shipment.TrackingNumber))
                    && !Changed(e, nameof(Shipment.Status)):
                    Add("Shipping", "shipment.tracking", "Info",
                        () => "Courier tracking updated",
                        () => $"Tracking information for order #{shipment.OrderId} was updated.",
                        () => $"/Admin/Shipments/Details/{shipment.Id}");
                    break;
                case Review review when e.State == EntityState.Added && !review.IsApproved:
                    Add("Reviews", "review.submitted", "Warning",
                        () => "New customer review awaiting moderation",
                        () => $"A new {review.Rating}-star review was submitted. Open the review to approve or reject it.",
                        () => $"/Admin/Reviews/Details/{review.Id}");
                    break;
                case Review review when e.State == EntityState.Modified &&
                    (Changed(e, nameof(Review.IsApproved)) || Changed(e, nameof(Review.IsRejected))):
                    var reviewStatus = review.IsRejected ? "rejected" : review.IsApproved ? "approved" : "pending";
                    Add("Reviews", $"review.{reviewStatus}", "Info",
                        () => $"Customer review {reviewStatus}",
                        () => $"A {review.Rating}-star review is now {reviewStatus}.",
                        () => $"/Admin/Reviews/Details/{review.Id}");
                    break;
                case ProductVariant variant when
                    (e.State == EntityState.Added && variant.StockQuantity <= 5) ||
                    (e.State == EntityState.Modified && Changed(e, nameof(ProductVariant.StockQuantity)) &&
                     ((e.Property(nameof(ProductVariant.StockQuantity)).OriginalValue is int before &&
                         before > 5 && variant.StockQuantity <= 5) ||
                      (e.Property(nameof(ProductVariant.StockQuantity)).OriginalValue is int old &&
                         old > 0 && variant.StockQuantity == 0))):
                    var remaining = variant.StockQuantity;
                    Add("Inventory", remaining == 0 ? "stock.out" : "stock.low",
                        remaining == 0 ? "Critical" : "Warning",
                        () => remaining == 0 ? $"Out of stock: {variant.SKU}" : $"Low stock: {variant.SKU}",
                        () => $"Variant {variant.SKU} now has {remaining} units available.",
                        () => $"/Admin/Products/Edit/{variant.ProductId}");
                    break;
                case ProductVariant variant when e.State == EntityState.Modified &&
                    Changed(e, nameof(ProductVariant.StockQuantity)) &&
                    e.Property(nameof(ProductVariant.StockQuantity)).OriginalValue is int previous &&
                    previous <= 5 && variant.StockQuantity > 5:
                    Add("Inventory", "stock.replenished", "Success",
                        () => $"Inventory replenished: {variant.SKU}",
                        () => $"Variant {variant.SKU} has been replenished to {variant.StockQuantity} units.",
                        () => $"/Admin/Products/Edit/{variant.ProductId}");
                    break;
                case Product product when e.State == EntityState.Added:
                    Add("Catalog", "product.created", "Info",
                        () => "Product added to catalog",
                        () => $"Product '{product.Name}' was created.",
                        () => $"/Admin/Products/Edit/{product.Id}");
                    break;
                case Product product when Changed(e, nameof(Product.IsPublished)):
                    var published = product.IsPublished;
                    Add("Catalog", "product.publication", published ? "Success" : "Warning",
                        () => published ? "Product published" : "Product unpublished",
                        () => $"Product '{product.Name}' was {(published ? "published" : "unpublished")}.",
                        () => $"/Admin/Products/Edit/{product.Id}");
                    break;
                case Product product when e.State == EntityState.Modified &&
                    (Changed(e, nameof(Product.BasePrice)) || Changed(e, nameof(Product.Name))):
                    Add("Catalog", "product.updated", "Info",
                        () => "Product listing updated",
                        () => $"Catalog details for '{product.Name}' were updated.",
                        () => $"/Admin/Products/Edit/{product.Id}");
                    break;
                case ApplicationUser customer when e.State == EntityState.Added:
                    Add("Customers", "customer.registered", "Info",
                        () => "New customer account",
                        () => $"A new store account was created for {customer.Email ?? customer.UserName ?? "a customer"}.",
                        () => $"/Admin/Customers/Details/{Uri.EscapeDataString(customer.Id)}");
                    break;
                case ApplicationUser customer when Changed(e, nameof(ApplicationUser.IsActive)):
                    var isActive = customer.IsActive;
                    Add("Customers", "customer.access", isActive ? "Info" : "Warning",
                        () => isActive ? "Customer access restored" : "Customer account deactivated",
                        () => $"Account access for {customer.Email ?? customer.UserName ?? "a customer"} changed.",
                        () => $"/Admin/Customers/Details/{Uri.EscapeDataString(customer.Id)}");
                    break;
                case Coupon coupon when e.State == EntityState.Added:
                    Add("Promotions", "coupon.created", "Info",
                        () => "New discount coupon",
                        () => $"Coupon '{coupon.Code}' was created.",
                        () => $"/Admin/Coupons/Edit/{coupon.Id}");
                    break;
                case Coupon coupon when Changed(e, nameof(Coupon.IsActive)) ||
                    Changed(e, nameof(Coupon.ExpiresAt)):
                    Add("Promotions", "coupon.updated", "Info",
                        () => "Coupon availability updated",
                        () => $"Availability settings for coupon '{coupon.Code}' were changed.",
                        () => $"/Admin/Coupons/Edit/{coupon.Id}");
                    break;
                case HeroSlide slide when e.State == EntityState.Added ||
                    Changed(e, nameof(HeroSlide.IsActive)):
                    Add("Content", "slide.updated", "Info",
                        () => "Homepage slide updated",
                        () => $"Slide '{slide.Title}' was {(slide.IsActive ? "added or activated" : "deactivated")}.",
                        () => "/Admin/Banners");
                    break;
                case Banner banner when e.State == EntityState.Added ||
                    Changed(e, nameof(Banner.IsActive)):
                    Add("Content", "banner.updated", "Info",
                        () => "Storefront banner updated",
                        () => $"Banner '{banner.Title}' was {(banner.IsActive ? "added or activated" : "deactivated")}.",
                        () => "/Admin/Banners");
                    break;
                case SiteSettings setting when e.State == EntityState.Added ||
                    (e.State == EntityState.Modified && Changed(e, nameof(SiteSettings.Value))):
                    Add("Settings", "settings.updated", "Info",
                        () => "Store setting changed",
                        () => $"Store configuration '{setting.Key}' was updated. Values are not included in notifications.",
                        () => "/Admin/Settings");
                    break;
                case NewsletterSubscriber subscriber when e.State == EntityState.Added:
                    Add("Marketing", "newsletter.subscribed", "Info",
                        () => "New newsletter subscriber",
                        () => "A customer subscribed to store updates.",
                        () => "/Admin/Dashboard");
                    break;
                case InventoryTransaction inventory when e.State == EntityState.Added &&
                    inventory.OrderId == null:
                    Add("Inventory", "inventory.adjusted", "Info",
                        () => "Inventory adjustment recorded",
                        () => $"Stock for variant #{inventory.ProductVariantId} was manually adjusted by {inventory.QuantityChange} units.",
                        () => "/Admin/Products");
                    break;
                case Brand brand when e.State == EntityState.Added:
                    Add("Catalog", "brand.created", "Info",
                        () => "Brand added",
                        () => $"Brand '{brand.Name}' was created.",
                        () => "/Admin/Brands");
                    break;
                case Category category when e.State == EntityState.Added:
                    Add("Catalog", "category.created", "Info",
                        () => "Category added",
                        () => $"Category '{category.Name}' was created.",
                        () => "/Admin/Categories");
                    break;
                case Collection collection when e.State == EntityState.Added:
                    Add("Catalog", "collection.created", "Info",
                        () => "Collection added",
                        () => $"Collection '{collection.Name}' was created.",
                        () => "/Admin/Collections");
                    break;
            }
        }
        return drafts;
    }

    private static bool Changed(EntityEntry entry, string field) =>
        entry.State == EntityState.Modified &&
        entry.Property(field).IsModified &&
        !Equals(entry.Property(field).OriginalValue, entry.Property(field).CurrentValue);

    private static string Clip(string value, int length)
        => value.Length <= length ? value : value[..length];
}
