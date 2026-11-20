> **Updated bKash/COD checkout integration:** See [`BKASH_INTEGRATION_README.md`](BKASH_INTEGRATION_README.md). The earlier payment notes below describe the project before this bKash work and are superseded by that file.

# PANENKA / Jersey-ecommerce — storefront/backend integration

This project retains the supplied ASP.NET Core MVC / Identity / EF Core / SQL Server admin application and the original PANENKA black-and-white storefront styling. The customer-facing functionality is backed by the **existing database models**. No payment gateway is included.

## Run on your Windows .NET 10 development machine

1. Open `Jersey-ecommerce.slnx` in Visual Studio with .NET 10 tooling, or open a terminal in the project directory.
2. Verify `ConnectionStrings:DefaultConnection` in `appsettings.json` points to your SQL Server instance and that your existing database is accessible.
3. Execute:
   ```powershell
   dotnet restore
   dotnet build
   dotnet ef database update
   dotnet run
   ```
4. Log in with your existing admin account. Under **Admin > Settings**, verify the standard delivery charge (`shipping.standard_fee`), free-delivery threshold, and cash-on-delivery toggle. If no charge is configured, the existing standard shipping setting defaults to `0` (free delivery); set a real charge before accepting orders.
5. Publish products, create their size/color variants, activate categories, and enter variant stock. Only `IsPublished` merchandise shows on the public store.
6. Use two separate browser profiles (Admin and Customer) for checkout testing.

**Migration note:** The three experimental `CartPromotionSelection`, `OrderDeliverySnapshot`, and `CheckoutSubmission` models were removed from the incomplete integration. The corrected storefront uses the existing `ShoppingCart`, `CartItem`, `Address`, `Order`, `OrderItem`, `Payment`, `Shipment`, `OrderStatusHistory`, `Coupon`, `CouponUsage`, and `InventoryTransaction` tables. The existing `ProductVariantImage` migration from the provided project still applies. There is **no new migration for this integration**.

**Payment note:** Cash on delivery records `Payment.Status = Pending` until collection is verified. Outside-Dhaka checkout selects `ManualAdvance`, also Pending until an admin independently verifies external payment at `/Admin/CheckoutPaymentReview`. No card/mobile gateway, webhook, charging, or refund processing is implemented; these remain future integrations. The customer's payment reference is *not* proof of payment.

## Customer routes

| Page / API | What it uses |
| --- | --- |
| `/` | Admin published products/new arrivals, HeroSlides, active Home banners, active Collections, public SEO/social settings, cart count, wishlist state, newsletter subscriber API |
| `/shop` | Existing `ShopCatalogApiController` at `GET /api/storefront/shop`; search, category, **real Collection membership**, size, stock, price, sort, pagination; optional `GET /api/storefront/shop/banner` |
| `/Product/Details/{slug}` | Product gallery, size/color combinations, optional variant images, live stock and price, add-to-bag and wishlist, approved reviews; review submission after delivered purchase |
| `/Cart` | Authenticated, SQL-backed bag, quantities, remove, coupon, wishlist, suggested products |
| `/Checkout/Delivery` | Address prefill from default saved address, delivery fee from Admin Settings, server-side validation |
| `/Checkout/Payment` | COD (Dhaka when enabled) and manual advance only; idempotent order number from checkout form token, guarded inventory decrement and inventory transaction audit |
| `/Checkout/Success/{id}` | Authenticated order confirmation, no payment auto-verification |
| `/MyOrders` and `/MyOrders/{id}` | Live order status, payment, shipment, snapshot shipping address already stored in existing Order columns |
| `/Wishlist` | Authenticated saved products, remove item |
| `/MyAddresses` | Customer's own saved addresses (add, edit, set default, delete) |
| `/Home/Contact` | Public customer support contact information managed in Admin Settings |
| `/Account/Profile` | Existing profile functionality plus links to orders, addresses and wishlist |
| `POST /api/storefront/newsletter` | Adds/reactivates existing NewsletterSubscriber records, CSRF validated |

## Admin connections

- **Products/Categories/Brands/Variants/Collections:** public pages read active and published catalog entities; variant stock, SKU, optional images and prices come from SQL Server.
- **Banners:** live Home HeroSlides and Home/Shop banner placements (subject to active/date settings) update the storefront.
- **Coupons:** validated against active dates, global/per-user limits, minimum spend and discount rules. Applied promo selection is stored in the user's **server session** (not a new table); coupon usage is recorded against the placed order.
- **Settings:** checkout reads maintenance and COD switches and the existing standard shipping fee/free shipping threshold; homepage reads configured social/SEO settings.
- **Orders/Payments/Shipments:** placing an order creates records in all three existing tables; status and tracking are shown to customers. Manual payment verification is available at `/Admin/CheckoutPaymentReview`.
- **Inventory:** checkout uses existing `InventoryStockService`, records a `Sale` transaction for each variant and avoids overselling. Cancelling an unpaid COD order restores stock with an inventory `Restock` transaction, subject to unpaid/pending/unshipped checks. Manual-advance cancellations need staff review.
- **Reviews:** eligible delivered-order customers can submit one review per product. It requires existing admin approval before being publicly displayed.
- **Customers:** individual carts, wishlists, addresses and orders are scoped to the signed-in Identity user.

## Suggested verification sequence

1. Check `/api/storefront/shop` and `/api/storefront/home` return JSON (without duplicate routes); `/shop` and `/` display published SQL products, not demo merchandise.
2. Log in as Customer and add a real active size/color variant; confirm it persists after refresh, including selected variant images when available.
3. Open `/Cart`; update quantity; remove item; test a valid/invalid coupon; visit `/Wishlist` and `/MyAddresses`.
4. Set a known `shipping.standard_fee` and `shipping.free_threshold` in Admin Settings and verify totals in Delivery and Payment.
5. Place a **test COD order for Dhaka**. Confirm it appears in Admin Orders, Payments, Shipments, Inventory Transactions, and customer My Orders; confirm inventory decreased exactly once.
6. Place an **Outside Dhaka** test order with ManualAdvance. Confirm payment remains Pending and no gateway is called. Independently verify a test transfer before using the admin verification page.
7. Cancel an unpaid pending COD test order; check that stock is restored and its inventory transaction recorded exactly once.
8. Deliver a test order through the admin workflow and submit a review with that customer; approve it in Admin Reviews and confirm it appears on the product page.

## Integration checks and limitations

- Confirmed that `api/storefront/shop` and `api/storefront/cart` have one route owner each.
- Confirmed customer-facing Razor pages and shared partials are in standard MVC view lookup locations.
- JavaScript syntax checked with Node.js for the Home, Shop, Cart and Product scripts.
- Static controller/view reference and removed-model scans passed.
- **A full `dotnet build`, EF migrations, real SQL Server integration tests and browser E2E checkout were not runnable in the current environment** (no .NET SDK or access to the local SQL Server). You must perform the build and checkout verification steps above on your development machine. This is a source-code integration, not a verified production deployment.

### Notes

- ASP.NET Core session uses in-memory distributed cache. For a multi-server production deployment, switch to a shared Redis/SQL distributed cache and coordinate session key protection; otherwise selected coupon and in-progress delivery state may be lost on a different node/restart.
- Coupon selection is per authenticated user **and browser session**; it does not persist across browsers. Coupon usage on a completed order is durable.
- Delivery fee overrides can be specified in `Checkout:DhakaShippingFee` and `Checkout:OutsideDhakaShippingFee` (decimal amounts) in appsettings, otherwise Admin's existing standard shipping fee is used for both regions. Don't leave rates unset if delivery should be chargeable.
- Enable appropriate HTTPS, anti-CSRF, backups, monitoring, cookie persistence, and hosting secrets management before going live.
