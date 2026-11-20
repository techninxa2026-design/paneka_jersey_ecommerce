# PANENKA — Admin Notification Center

## Scope and UI

The shared ASP.NET Core MVC Admin layout has a bell at the top right and **Overview → Notifications** in the sidebar. The unread badge is individual to each administrator.

The bell shows seven latest events with category, severity, timestamp (Bangladesh local time), and a link to the affected business record. The badge refreshes every 30 seconds while the browser tab is visible; opening the dropdown retrieves the latest events. No external SMS, WhatsApp, push notification, bKash gateway, or email service is used.

**Admin → Notifications** is the complete persistent activity center with 25 records per page, text search, category filter, unread/read filter, mark one read, mark one unread, mark all read, and links back to the affected Orders/Payments/Shipments/Reviews/Products/Customers/etc pages.

All endpoints require the **Admin** role. There is no customer-facing endpoint. Customer records and payment references are not copied wholesale into alerts.

## Event coverage

This is database-persisted, significant business activity monitoring rather than every cart hover or browsing action.

- **Orders:** new order, allowed order-status changes, cancellation, deletion warnings
- **Payments:** bKash submitted, staff verification (Paid), rejection, refunds or failures, payment-record deletion warnings
- **Shipping:** packing, dispatch, delivery, return/failure, courier-tracking edits and deletions
- **Reviews:** new customer review requiring moderation, approval/rejection and deletion
- **Inventory:** crossing the low-stock threshold (5 units), stock reaching zero, restock above threshold, manual inventory adjustment, removed variants
- **Customers:** registration/new account, activation/deactivation and deletions
- **Catalog:** new products, publication changes, product name/price changes, brand/category/collection additions or deletions
- **Promotions:** coupon creation, availability/expiration changes, coupon deletions
- **Content and settings:** homepage banners/slides changes, store settings changes (setting keys only; **never secret values**)
- **Marketing:** new newsletter subscribers

The notifier is attached centrally to the `ApplicationDbContext` save boundary. A successful business change generates its linked notification in the same database transaction. Failed and rolled-back relational writes leave no phantom messages. Notifications do not generate new notifications when read/unread changes are saved.

The notification severity is **Info**, **Success**, **Warning**, or **Critical**. Low-stock notifications only trigger when the stock first crosses below five units (and again when it reaches zero) instead of once for every unit sold. Verification and corresponding order confirmation in the same save produce a single payment alert; shipment/order progression in the same save is also deduplicated.

## Data ownership and privacy

Two additive SQL Server tables:
- `AdminNotifications`: globally shared event journal, category, event code, severity, concise description, internal admin route, UTC timestamp
- `AdminNotificationReads`: per-admin read receipt with unique (AdminUserId, AdminNotificationId)

One admin opening or reading an event must **not** clear another administrator's unread count. Read/unread is idempotent; the database has a unique constraint to protect against duplicate receipt submissions.

Notifications are retained as activity history and cannot be deleted through the admin UI. This is a convenience/operational timeline, **not** a substitute for financial accounting or a tamper-proof security audit.

## Initial rollout on existing production data

The migration creates new tables without changing existing Orders, Payments, Products, or customer data. It seeds a *bounded* set of unresolved bKash transfer submissions (up to 300), unmoderated reviews (up to 200), and active published low-stock products (up to 200) at migration time, so administrators see real outstanding work on the first login.

### Database upgrade — required

Back up the client SQL Server database before production migration.

```powershell
git switch main
git pull --ff-only origin main
```

In Visual Studio Package Manager Console, select the **Jersey-ecommerce** default project and run:

```powershell
Update-Database -Context ApplicationDbContext
```

This applies `20261120173000_AdminNotificationCenter` if the database is already up to date with the manual bKash migration. Verify the database and existing migration history; do not replace the production DB.

Restart/redeploy the app after applying migrations. The shared admin layout queries these new tables; code deploy without the new migration will cause notification query failures.

### Staging acceptance checklist

- [ ] An existing admin opens Dashboard and sees a bell; Customers cannot access `/Admin/Notifications`, `/Summary`, `/Popup`, or any read/write endpoint.
- [ ] Create a new order; the admin gets a single `order.created` alert linked to the correct order.
- [ ] Submit a bKash TrxID; see `Payment needs verification`, then verify it; see `Paid` alert and linked order Confirmed without an extra duplicate Confirmed event.
- [ ] Reject another bKash claim and verify that the alert links back to that payment and its rejection state.
- [ ] Update shipping from Pending to Packed to Shipped to Delivered and check shipping alerts and customer shipment visibility.
- [ ] Submit, approve or reject a review; ensure moderation alerts are recorded.
- [ ] Reduce product stock from 6 to 5, 5 to 4, 4 to 0, and 0 to 7: alerts for low, out, replenished, but not for every unit.
- [ ] Register a customer; update their account active status; confirm account alerts and safe links.
- [ ] Create or edit a product, coupon, banner and store setting; confirm relevant activity. No store secret values appear.
- [ ] Sign in as two distinct Admin users; read all as Admin A and confirm Admin B still has unread alerts.
- [ ] Test individual mark unread/read, search/category/status filters and pagination.
- [ ] Open an alert to its exact Admin record; confirm it is marked read only for the acting administrator.
- [ ] Check the notification bell auto-refresh after events created in another browser.
- [ ] Try rolling back a test transaction; no corresponding persisted notification remains.
- [ ] Test mobile admin header, keyboard navigation, and session expiration during polling.

**Validation boundary:** automated .NET/Razor/EF-snapshot tests do not replace a staging SQL Server migration and live two-browser acceptance check. No external push-notification service is installed.
