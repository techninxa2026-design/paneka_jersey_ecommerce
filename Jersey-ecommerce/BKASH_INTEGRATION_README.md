# PANENKA — Integrated COD + bKash Tokenized Checkout v2 (Sandbox)

## What changed

The supplied full `Jersey-ecommerce` project was used as the base. Storefront styles, catalog, admin products, stock, coupons, customer accounts, assets, and other modules were retained. The following checkout files were repaired:

- `Models/Checkout/PlaceOrderInput.cs` is now the **only** checkout input class; the duplicate nested class was removed from `Areas/Admin/ViewModels/CheckoutViewModels.cs`.
- `Controllers/CheckoutController.cs` uses live `CheckoutService.ShippingRates` (handles nullable regional overrides), accepts COD or bKash, and uses the existing `OrderNumber`-based idempotency rather than referencing an undefined `CheckoutSubmission` table.
- `Areas/Admin/Services/CheckoutService.cs` allows COD nationwide when Admin > Settings enables COD, and creates `Payment(Method=Bkash, Status=Pending)` inside the same serializable transaction that creates the order, shipment, coupon usage and stock movements.
- `Controllers/BkashController.cs` starts a hosted tokenized bKash attempt; verifies results with an authenticated server-side `payment/status` call before changing a payment to Paid; records a `PaymentTransaction` audit row; and updates both Order.PaidAt and Payment.PaidAt transactionally. It does **not** create fake successful payments.
- `Views/Checkout/Delivery.cshtml`, `Views/Checkout/Payment.cshtml`, and `Views/Checkout/Success.cshtml` were updated without changing the PANENKA CSS theme.
- `Areas/Admin/Services/OrderCancellationService.cs` prevents customers from cancelling an online payment still awaiting reconciliation. Admin staff should verify remotely before cancelling.

The project's existing `Data/ApplicationDbContext.cs`, `Models/Checkout/BkashPaymentAttempt.cs`, migration `Migrations/20261020070153_bkashpayment.cs` and startup gateway registrations are retained. **No additional CheckoutSubmission migration is necessary:** your original service intentionally uses a unique order number derived from the signed-in checkout submission token. The database's unique OrderNumber index plus the service's replay check perform that responsibility.

## Before running locally

Install .NET SDK **10**, SQL Server accessible via the project's existing `DefaultConnection`, and `dotnet-ef` matching EF Core 10. Do not paste merchant secrets into appsettings.json or the frontend.

From the project directory:

```powershell
dotnet restore
dotnet build
dotnet ef database update
dotnet run
```

**Important:** the supplied project already has migration `20261020070153_bkashpayment` for `BkashPaymentAttempts`. Check `__EFMigrationsHistory`, review the migration on a backup/staging database, and apply it if outstanding. Do not generate another migration for a CheckoutSubmission table. If your current deployed schema differs from the uploaded code, inspect migrations before applying them.

## bKash sandbox credentials (merchant-issued)

The four credentials are **not** a shared public bKash sandbox key. Obtain merchant sandbox App Key, App Secret, Username and Password from bKash during onboarding. Until provided, the bKash button displays a friendly configuration error. COD can still work without credentials.

Use project user-secrets for local development (the `.csproj` has a UserSecretsId):

```powershell
dotnet user-secrets set "Bkash:AppKey" "YOUR_SANDBOX_APP_KEY"
dotnet user-secrets set "Bkash:AppSecret" "YOUR_SANDBOX_APP_SECRET"
dotnet user-secrets set "Bkash:Username" "YOUR_SANDBOX_USERNAME"
dotnet user-secrets set "Bkash:Password" "YOUR_SANDBOX_PASSWORD"
dotnet user-secrets set "Bkash:CallbackUrl" "https://YOUR-PUBLIC-HTTPS-DOMAIN/Checkout/Bkash/Callback"
```

The preconfigured **sandbox base URL** is:

```
https://tokenized.sandbox.bka.sh/v2/tokenized-checkout
```

The `CallbackUrl` must be publicly accessible **HTTPS**, not a `localhost` URL. Sandbox testing from a local PC therefore needs an HTTPS tunnel or a staging deployment. Keep `BaseUrl` sandbox until you receive approved production credentials; live URL is `https://tokenized.pay.bka.sh/v2/tokenized-checkout` once provided/confirmed by bKash.

Official bKash sandbox merchant demo: https://merchantdemo.sandbox.bka.sh/tokenized-checkout/version/v2 — wallet **01770618575**, OTP **123456**, PIN **12121**. These are *demo customer wallet* values, not API merchant credentials.

For production, store the four keys in hosting secrets / environment variables, for example `Bkash__AppKey`, `Bkash__AppSecret`, `Bkash__Username`, `Bkash__Password`; never commit actual values or send them to frontend JavaScript.

## Payment routes and business rules

| Route | Behavior |
|---|---|
| `/Checkout/Delivery` | Address, region and shipping fee calculated using Admin settings |
| `/Checkout/Payment` | COD for Dhaka AND Outside Dhaka when globally enabled; bKash online |
| `POST /Checkout/Payment` | Validated anti-forgery, login, submission token; atomic order + Pending payment |
| `/Checkout/Bkash/Start/{id}` | Authenticated customer starts/re-enters their pending bKash attempt |
| `/Checkout/Bkash/Callback?...` | Hosted bKash return; server calls Execute then Query before Paid |
| `/Checkout/Bkash/Verify/{id}` | Customer can check an already-created payment with bKash |
| `/Checkout/Bkash/Status/{id}` | Pending/unconfirmed payment information |
| `/Checkout/Success/{id}` | bKash payment requires Paid before success screen; COD shows Pending |

Note that the callback does **not** automatically refund, cancel, or unreserve stock after a failed payment: the associated order remains pending until safely reconciled/cancelled. This avoids losing paid orders if an execute/query response is delayed.

### Test scenarios before launch

1. Dhaka COD on: creates order/payment/stock movement with `Method=CashOnDelivery`, `Status=Pending`; no gateway call.
2. Outside Dhaka COD on: behaves the same, charges the outside-Dhaka shipping rate.
3. Admin COD off: COD disappears from checkout for *both* regions, bKash remains selectable.
4. Without sandbox credentials: bKash POST rejects *before* allocating stock or placing an order.
5. Sandbox bKash success: Pending -> gateway redirect -> callback Execute/Query -> Paid, Order.PaidAt, Payment.PaidAt, PaymentTransaction and MyOrders; callback repeats do not create duplicate audit rows.
6. Sandbox cancel/failure: order and payment remain Pending, with a status/reconciliation screen and **no** false Paid flag.
7. Double-submit payment form: same submission token yields one `PNK-...` order and one set of inventory movements.
8. Coupon/free-shipping and differing regional shipping amounts: delivery/payment/final order use the same Admin rates (with nullable overrides).
9. Admin order fulfillment: Bkash/ManualAdvance cannot ship while unpaid; COD can continue through normal fulfillment.
10. Open the hosted callback in an unauthenticated browser: it cannot mark a payment Paid from query-string flags alone.

## Validation / limitations

- Source files were checked for a single `PlaceOrderInput`, no missing `CheckoutSubmission` references, migration presence, correct gateway routes, and the payment-page JS passed `node --check` with Razor expressions replaced by test constants.
- **No `dotnet` executable or SQL Server/bKash merchant credentials are available in the processing environment; a full compile, EF migration execution, gateway charge, and real end-to-end run were NOT performed.** Validate locally with `dotnet build`, staging SQL Server, and bKash-issued sandbox credentials before deployment.
- Token cache is in-memory. Configure a production-grade shared/distributed cache and review concurrent gateway-start handling if deploying multiple app instances. Reconcile remotely uncertain payment attempts before issuing another charge; never trust client-submitted reference IDs as payment proof.
- Merchants need a dedicated payment reconciliation/refund and order-expiry process to manage stuck pending orders and avoid reserving stock indefinitely.
