using System.Data;
using Jersey_ecommerce.Data;
using Jersey_ecommerce.Models.Checkout;
using Jersey_ecommerce.Models.Catalog;
using Jersey_ecommerce.Models.Customers;
using Jersey_ecommerce.Models.Orders;
using Jersey_ecommerce.Models.Promotions;
using Jersey_ecommerce.Models.Shopping;
using Jersey_ecommerce.ViewModels.Checkout;
using Jersey_ecommerce.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jersey_ecommerce.Services.Checkout;

// Uses ONLY the existing catalog, shopping, orders, coupon, address and inventory models.
// Promo choice stays in the authenticated user's server session until checkout is completed.
public sealed class CheckoutService(
    ApplicationDbContext db, IOptions<CheckoutOptions> options,
    IHttpContextAccessor http, InventoryStockService stock)
{
    public CheckoutOptions Options => options.Value;
    private string PromoKey(string userId) => "PANENKA_PROMO_" + userId;
    private string? SelectedCoupon(string userId) => http.HttpContext?.Session.GetString(PromoKey(userId));

    public async Task<bool> InMaintenance() =>
        string.Equals(await db.SiteSettings.AsNoTracking().Where(s => s.Key == "operations.maintenance")
            .Select(s => s.Value).FirstOrDefaultAsync(), "true", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> CodEnabled()
    {
        var value = await db.SiteSettings.AsNoTracking().Where(s => s.Key == "operations.cash_on_delivery")
            .Select(s => s.Value).FirstOrDefaultAsync();
        return value is null || (bool.TryParse(value, out bool enabled) && enabled);
    }

    private async Task<decimal> StoreMoney(string key)
    {
        var raw = await db.SiteSettings.AsNoTracking().Where(s => s.Key == key)
            .Select(s => s.Value).FirstOrDefaultAsync();
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : 0m;
    }

    public async Task<(decimal Dhaka, decimal OutsideDhaka)> ShippingRates(decimal subtotal)
    {
        var standard = await StoreMoney("shipping.standard_fee");
        var freeFrom = await StoreMoney("shipping.free_threshold");
        if (freeFrom > 0 && subtotal >= freeFrom) return (0m, 0m);
        return (Options.DhakaShippingFee ?? standard, Options.OutsideDhakaShippingFee ?? standard);
    }

    private async Task<ShoppingCart?> FindCart(string userId, bool create = false)
    {
        var cart = await db.ShoppingCarts.SingleOrDefaultAsync(x => x.UserId == userId);
        if (cart != null || !create) return cart;
        cart = new ShoppingCart { UserId = userId };
        db.ShoppingCarts.Add(cart);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            db.Entry(cart).State = EntityState.Detached;
            cart = await db.ShoppingCarts.SingleAsync(x => x.UserId == userId);
        }
        return cart;
    }

    private static string? SafeUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith('/') && !path.StartsWith("//") && !path.Contains('\\')) return path;
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? path : null;
    }

    private static string? MainImage(Product product)
        => SafeUrl(product.Images.Where(i => !((i.AltText ?? "").StartsWith("variant:", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                .Select(i => i.ImageUrl).FirstOrDefault());

    public async Task<CartSummaryDto> GetCart(string userId, bool validateStock = false)
    {
        var cartId = await db.ShoppingCarts.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync();
        if (!cartId.HasValue) return new CartSummaryDto();

        var items = await db.CartItems.AsNoTracking().Where(x => x.ShoppingCartId == cartId.Value)
            .Include(x => x.ProductVariant).ThenInclude(v => v.Size)
            .Include(x => x.ProductVariant).ThenInclude(v => v.Color)
            .Include(x => x.ProductVariant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .Include(x => x.ProductVariant).ThenInclude(v => v.Product).ThenInclude(p => p.Category)
            .OrderBy(x => x.AddedAt).AsSplitQuery().ToListAsync();
        var variantIds = items.Select(x => x.ProductVariantId).ToList();
        var images = await db.ProductVariantImages.AsNoTracking()
            .Where(x => variantIds.Contains(x.ProductVariantId))
            .ToDictionaryAsync(x => x.ProductVariantId, x => x.ImageUrl);
        var lines = items.Select(x =>
        {
            var variant = x.ProductVariant;
            var product = variant.Product;
            images.TryGetValue(variant.Id, out var variantImage);
            return new CartLineDto
            {
                Id = x.Id, VariantId = variant.Id, ProductId = product.Id,
                Name = product.Name, Slug = product.Slug,
                Category = product.Category?.Name ?? "Jersey",
                Size = variant.Size?.Name ?? "", Color = variant.Color?.Name ?? "",
                ImageUrl = SafeUrl(variantImage) ?? MainImage(product),
                Price = variant.Price, Quantity = x.Quantity,
                StockQuantity = product.IsPublished && variant.IsActive && variant.Size.IsActive && variant.Color.IsActive
                    ? variant.StockQuantity : 0,
                IsNewArrival = product.IsNewArrival
            };
        }).ToList();
        if (validateStock && lines.Any(i => i.StockQuantity < i.Quantity || i.Quantity <= 0))
            throw new CheckoutException("Some items are no longer available in the requested quantity. Please update your bag.");
        var subtotal = lines.Sum(x => x.Price * x.Quantity);
        var code = SelectedCoupon(userId);
        decimal discount = 0;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var coupon = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code);
            if (coupon != null && await CouponValid(coupon, userId, subtotal))
                discount = CalculateDiscount(coupon, subtotal);
            else if (validateStock)
                throw new CheckoutException("Your coupon is no longer valid. Remove it in the bag and try again.");
            else code = null;
        }
        return new CartSummaryDto { Items = lines, DiscountAmount = discount, PromoCode = code };
    }

    public async Task Add(string userId, int productId, int variantId, int quantity)
    {
        if (quantity is < 1 or > 100) throw new CheckoutException("Quantity must be between 1 and 100.");
        if (await InMaintenance()) throw new CheckoutException("The store is temporarily in maintenance mode.");
        var variant = await db.ProductVariants.AsNoTracking().Include(v => v.Product)
            .Include(v => v.Size).Include(v => v.Color)
            .SingleOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId);
        if (variant is null || !variant.IsActive || !variant.Product.IsPublished ||
            !variant.Size.IsActive || !variant.Color.IsActive)
            throw new CheckoutException("This jersey variation is not available.");
        var cart = await FindCart(userId, create: true) ?? throw new CheckoutException("Could not create your bag.");
        var existing = await db.CartItems.SingleOrDefaultAsync(x => x.ShoppingCartId == cart.Id && x.ProductVariantId == variantId);
        var updated = (existing?.Quantity ?? 0) + quantity;
        if (updated > 100 || updated > variant.StockQuantity)
            throw new CheckoutException($"Only {variant.StockQuantity} unit(s) are available.");
        if (existing == null) db.CartItems.Add(new CartItem { ShoppingCartId = cart.Id, ProductVariantId = variantId, Quantity = quantity });
        else existing.Quantity = updated;
        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SetQuantity(string userId, int itemId, int quantity)
    {
        if (quantity is < 1 or > 100) throw new CheckoutException("Choose between 1 and 100 units.");
        var line = await db.CartItems.Include(x => x.ShoppingCart)
            .Include(x => x.ProductVariant).ThenInclude(v => v.Product)
            .SingleOrDefaultAsync(x => x.Id == itemId && x.ShoppingCart.UserId == userId);
        if (line == null) throw new CheckoutException("Bag item not found.");
        if (!line.ProductVariant.IsActive || !line.ProductVariant.Product.IsPublished || quantity > line.ProductVariant.StockQuantity)
            throw new CheckoutException("That quantity is not available.");
        line.Quantity = quantity;
        line.ShoppingCart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task Remove(string userId, int itemId)
    {
        var line = await db.CartItems.Include(x => x.ShoppingCart)
            .SingleOrDefaultAsync(x => x.Id == itemId && x.ShoppingCart.UserId == userId);
        if (line == null) throw new CheckoutException("Bag item not found.");
        db.CartItems.Remove(line);
        line.ShoppingCart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SetCoupon(string userId, string? rawCode)
    {
        var session = http.HttpContext?.Session ?? throw new CheckoutException("Session is unavailable.");
        var code = rawCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) { session.Remove(PromoKey(userId)); return; }
        if (code.Length > 50) throw new CheckoutException("Coupon code is too long.");
        var subtotal = (await GetCart(userId)).Subtotal;
        if (subtotal <= 0) throw new CheckoutException("Your bag is empty.");
        var coupon = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code);
        if (coupon == null || !await CouponValid(coupon, userId, subtotal))
            throw new CheckoutException("Coupon is invalid, expired, below minimum order value, or fully used.");
        session.SetString(PromoKey(userId), code);
    }

    private async Task<bool> CouponValid(Coupon coupon, string userId, decimal subtotal)
    {
        var now = DateTime.UtcNow;
        if (!coupon.IsActive || (coupon.StartsAt.HasValue && coupon.StartsAt.Value > now) ||
            (coupon.ExpiresAt.HasValue && coupon.ExpiresAt.Value <= now) ||
            subtotal < coupon.MinimumOrderAmount ||
            (coupon.UsageLimit.HasValue && coupon.UsageCount >= coupon.UsageLimit.Value)) return false;
        if (coupon.UsageLimitPerUser.HasValue &&
            await db.CouponUsages.AsNoTracking().CountAsync(x => x.UserId == userId && x.CouponId == coupon.Id) >= coupon.UsageLimitPerUser.Value)
            return false;
        return true;
    }

    private static decimal CalculateDiscount(Coupon coupon, decimal subtotal)
    {
        var discount = coupon.DiscountType == "Percentage"
            ? Math.Round(subtotal * coupon.DiscountValue / 100m, 2, MidpointRounding.AwayFromZero)
            : coupon.DiscountValue;
        if (coupon.MaximumDiscountAmount.HasValue) discount = Math.Min(discount, coupon.MaximumDiscountAmount.Value);
        return Math.Clamp(discount, 0m, subtotal);
    }

    public async Task<IReadOnlyList<object>> Variants(int productId)
    {
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == productId && v.Product.IsPublished && v.IsActive && v.Size.IsActive && v.Color.IsActive && v.StockQuantity > 0)
            .Include(v => v.Size).Include(v => v.Color)
            .OrderBy(v => v.Size.DisplayOrder).ThenBy(v => v.Color.Name).ToListAsync();
        var ids = variants.Select(v => v.Id).ToList();
        var images = await db.ProductVariantImages.AsNoTracking()
            .Where(x => ids.Contains(x.ProductVariantId)).ToDictionaryAsync(x => x.ProductVariantId, x => x.ImageUrl);
        return variants.Select(v =>
        {
            images.TryGetValue(v.Id, out var image);
            return (object)new { id = v.Id, productId, price = v.Price, stockQuantity = v.StockQuantity,
                size = v.Size.Name, color = v.Color.Name, colorHex = v.Color.HexCode, imageUrl = SafeUrl(image) };
        }).ToList();
    }

    public async Task<IReadOnlyList<object>> Recommendations(string userId)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsPublished && p.Variants.Any(v => v.IsActive && v.StockQuantity > 0))
            .Include(p => p.Category).Include(p => p.Images).Include(p => p.Variants)
            .OrderByDescending(p => p.CreatedAt).Take(8).AsSplitQuery().ToListAsync();
        return products.Select(p =>
        {
            var variant = p.Variants.First(v => v.IsActive && v.StockQuantity > 0);
            return (object)new { id = p.Id, productId = p.Id, variantId = variant.Id, name = p.Name,
                slug = p.Slug, category = p.Category?.Name ?? "Jersey", price = variant.Price,
                imageUrl = MainImage(p) };
        }).ToList();
    }

    // OrderNumber is derived from the signed-in session's unique checkout token, so duplicate POSTs
    // return the same order without requiring any new checkout tables or migration.
    public async Task<int> PlaceOrder(string userId, DeliveryInput delivery, PlaceOrderInput input)
    {
        if (delivery.Region is not ("Dhaka" or "OutsideDhaka")) throw new CheckoutException("Select a delivery region.");
        if (delivery.Region == "Dhaka" && !string.Equals(delivery.District?.Trim(), "Dhaka", StringComparison.OrdinalIgnoreCase))
            throw new CheckoutException("Inside Dhaka requires Dhaka district.");
        if (input.PaymentMethod is not ("COD" or "ManualAdvance" or "Bkash"))
            throw new CheckoutException("Choose a valid payment method.");
        // COD is available throughout Bangladesh when the store setting permits it.
        if (input.PaymentMethod == "COD" && !await CodEnabled()) throw new CheckoutException("Cash on delivery is disabled.");
        if (await InMaintenance()) throw new CheckoutException("Checkout is paused by the store.");
        if (!Guid.TryParseExact(input.SubmissionToken, "N", out _)) throw new CheckoutException("Invalid checkout token.");
        var orderNumber = "PNK-" + input.SubmissionToken[..24].ToUpperInvariant();
        // Existing order-number unique index is our durable checkout idempotency key.
        // Never allow a retry to silently change a previously recorded payment method.
        var previous = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId && o.OrderNumber == orderNumber)
            .Select(o => new { o.Id, Method = o.Payment != null ? o.Payment.Method : "" })
            .SingleOrDefaultAsync();
        if (previous is not null)
        {
            var expectedMethod = input.PaymentMethod switch
            {
                "COD" => "CashOnDelivery", "Bkash" => "Bkash", _ => "ManualAdvance"
            };
            if (previous.Method != expectedMethod)
                throw new CheckoutException("This checkout was already submitted using a different payment method. Check My Orders.");
            return previous.Id;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var bag = await FindCart(userId);
        if (bag is null) throw new CheckoutException("Your bag is empty.");
        var quote = await GetCart(userId, validateStock: true);
        if (quote.Items.Count == 0) throw new CheckoutException("Your bag is empty.");
        var shippingRates = await ShippingRates(quote.Subtotal);
        var shipping = delivery.Region == "Dhaka" ? shippingRates.Dhaka : shippingRates.OutsideDhaka;
        if (shipping < 0) throw new CheckoutException("Delivery fee is not configured correctly.");
        var now = DateTime.UtcNow;
        var order = new Order
        {
            UserId = userId, OrderNumber = orderNumber, Status = "Pending",
            CustomerName = delivery.FullName.Trim(), CustomerEmail = delivery.Email.Trim(),
            CustomerPhone = delivery.Phone.Trim(),
            ShippingAddressLine1 = delivery.AddressLine1.Trim(),
            ShippingAddressLine2 = delivery.AddressLine2?.Trim(),
            ShippingCity = delivery.City.Trim(), ShippingDistrict = delivery.District.Trim(),
            ShippingPostalCode = delivery.PostalCode?.Trim(), ShippingCountry = "Bangladesh",
            CustomerNote = delivery.Instructions?.Trim(),
            Subtotal = quote.Subtotal, DiscountAmount = quote.DiscountAmount,
            ShippingCost = shipping, TaxAmount = 0m, TotalAmount = quote.Total + shipping,
            CouponCode = quote.PromoCode, CreatedAt = now, UpdatedAt = now
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(); // order ID for linked inventory and payment records

        foreach (var line in quote.Items)
        {
            // Atomically lower inventory, recording StockBefore/StockAfter in existing InventoryTransactions.
            await stock.DecreaseForOrderAsync(line.VariantId, line.Quantity, order.Id);
            var sku = await db.ProductVariants.AsNoTracking().Where(v => v.Id == line.VariantId)
                .Select(v => v.SKU).SingleAsync();
            db.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id, ProductVariantId = line.VariantId,
                ProductName = line.Name, SKU = sku, SizeName = line.Size, ColorName = line.Color,
                ImageUrl = line.ImageUrl, Quantity = line.Quantity,
                UnitPrice = line.Price, LineTotal = line.Price * line.Quantity, DiscountAmount = 0m
            });
        }
        db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Method = input.PaymentMethod switch
            {
                "COD" => "CashOnDelivery",
                "Bkash" => "Bkash",
                _ => "ManualAdvance"
            },
            Status = "Pending",
            Provider = input.PaymentMethod == "Bkash" ? "bKash" : "Offline",
            Amount = order.TotalAmount,
            Currency = "BDT",
            TransactionReference = input.PaymentMethod == "ManualAdvance" ? input.PaymentReference?.Trim() : null,
            CreatedAt = now
        });
        db.Shipments.Add(new Shipment { OrderId = order.Id, Status = "Pending", CreatedAt = now });
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id, Status = "Pending", Note = "Order created from the PANENKA storefront.", CreatedAt = now
        });
        if (!string.IsNullOrEmpty(quote.PromoCode) && quote.DiscountAmount > 0)
        {
            var coupon = await db.Coupons.SingleAsync(c => c.Code == quote.PromoCode);
            if (!await CouponValid(coupon, userId, quote.Subtotal))
                throw new CheckoutException("Coupon is no longer valid; remove it from your bag.");
            var affected = await db.Coupons.Where(c => c.Id == coupon.Id &&
                (!c.UsageLimit.HasValue || c.UsageCount < c.UsageLimit.Value))
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.UsageCount, c => c.UsageCount + 1));
            if (affected != 1) throw new CheckoutException("Coupon redemption limit reached.");
            db.CouponUsages.Add(new CouponUsage
            {
                CouponId = coupon.Id, UserId = userId, OrderId = order.Id,
                DiscountAmount = quote.DiscountAmount, UsedAt = now
            });
        }
        // Save the customer's first delivery address for later repeat checkout.
        if (!await db.Addresses.AnyAsync(x => x.UserId == userId))
            db.Addresses.Add(new Address
            {
                UserId = userId, FullName = delivery.FullName.Trim(), PhoneNumber = delivery.Phone.Trim(),
                AddressLine1 = delivery.AddressLine1.Trim(), AddressLine2 = delivery.AddressLine2?.Trim(),
                City = delivery.City.Trim(), District = delivery.District.Trim(),
                PostalCode = delivery.PostalCode?.Trim(), Country = "Bangladesh", IsDefault = true
            });
        db.CartItems.RemoveRange(await db.CartItems.Where(x => x.ShoppingCartId == bag.Id).ToListAsync());
        bag.UpdatedAt = now;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        http.HttpContext?.Session.Remove(PromoKey(userId));
        return order.Id;
    }
}
