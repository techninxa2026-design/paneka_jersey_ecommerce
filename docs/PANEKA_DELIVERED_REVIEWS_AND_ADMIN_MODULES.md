# PANENKA | Delivered-order customer reviews and admin module audit

## Customer → Admin → Storefront review lifecycle

1. Admin verifies a manual bKash transfer (or progresses a valid COD order), processes the order and shipment, and changes the shipment to **Delivered** through the existing controlled fulfillment workflow. The customer's order detail page observes the changed status and refreshes (approximately every 20 seconds while the tab is visible).
2. The delivered-order page shows a prominent **How did your jersey fit?** callout and a direct **Rate & review this product** link beside every distinct purchased product. This is an invitation — delivery alone does not create a review.
3. `GET /MyOrders/{orderId}/Review/{productId}` checks the authenticated buyer, the actual delivered order, and that the ordered line contains the product. Other users cannot read or write a review on this order.
4. The customer chooses 1–5 stars, optional title (maximum 180 characters), and a meaningful comment (10–2000 characters). `POST` is anti-forgery protected and re-checks purchase, delivery and uniqueness on the server. The shared `CustomerReviewService` also handles the existing product-page review action.
5. A new `Review` is saved with `IsApproved = false` and `IsRejected = false`. The Admin Notification Center records **review.submitted**. It appears immediately under **Admin → Reviews** with status **Pending**.
6. Admin opens the review, sees customer/product/feedback, and selects **Approve** or **Reject** (protected POST). Approval causes the same real `Review` to appear on public `Product/Details/{slug}#reviews`. Pending and rejected reviews are never published publicly.
7. The buyer's delivered order and review page display **Pending**, **Approved** or **Rejected**. A rejected review can be corrected and resubmitted, reusing the original unique user/product record and re-entering the moderation queue. Already-pending or approved reviews cannot be duplicated.
8. An admin can also move a review back to Pending or delete it via the existing moderation UI. If a purchased product is no longer publicly listed, customer moderation status still works; the public product link is suppressed to avoid broken navigation.

## Sidebar module coverage

The admin sidebar currently exposes the following 16 protected modules, each with an actual MVC index action and role-based authorization:

| Admin module | Customer-facing or operational connection |
|---|---|
| Dashboard | Store sales, orders, inventory, reviews and operational KPI data |
| Notifications | Admin-only cross-module business activity journal |
| Products | Shop, product detail, product size/color and inventory |
| Categories | Shop navigation and category filters |
| Brands | Product brand information and catalog |
| Collections | Storefront collections and shop listings |
| Sizes & Colors | Product variants, stock, bag and checkout |
| Orders | Customer My Orders, status progress, cancellation and reviews after delivery |
| Payments | COD/manual bKash receipt and verification |
| Verify bKash | Admin verification → Paid payment → Confirmed order |
| Shipments | Courier tracking, order progress, customer delivery status |
| Customers | Account registration, profile and order ownership |
| Reviews | Delivered-buyer reviews → moderation → public product feedback |
| Coupons | Cart/checkout discounts and usage records |
| Banners & Slides | Scheduled storefront promotional media |
| Settings | Shipping, manual bKash receiving settings and storefront configuration |

In addition to earlier cart/checkout/shipment/payment regression coverage, this change adds checks that all sixteen module index actions **actually execute against a relational EF Core database**, each route is attached to an Admin-authorized controller, and every HTTP POST action in the sidebar controllers has anti-forgery protection.

This smoke coverage checks admin index data loading, not every individual file upload, production payment transfer, JavaScript click, or every CRUD edge case; those require a full staging browser pass.

## Staging checklist for client delivery

- [ ] Deliver COD and bKash orders through actual Admin → Orders and Shipments workflow. Confirm customers see the Delivered state without a fresh sign-in.
- [ ] Confirm a prominent review prompt appears on the delivered customer's order detail page, including multiple distinct products.
- [ ] Before delivery, try opening the review URL directly: the backend must return Not Found.
- [ ] Try the same order/review URL with a different customer: no data is leaked.
- [ ] Submit a 5-star review: check that Admin → Reviews shows Pending, and that the admin notification bell records the submission.
- [ ] Before approving, confirm shoppers cannot see the feedback on the public product page.
- [ ] Approve the review: check it appears publicly, and the customer's review page says Published.
- [ ] Try submitting a duplicate approved review: no additional row appears.
- [ ] Reject a different review: check that the customer can edit/resubmit, admin sees it Pending, and the corrected version can be approved.
- [ ] Open Admin Dashboard, Notifications, Products, Categories, Brands, Collections, Sizes & Colors, Orders, Payments, Verify bKash, Shipments, Customers, Reviews, Coupons, Banners & Slides, Settings.
- [ ] Exercise actual create, edit, delete/disable and search/filter workflows for each management module with the staging database and existing sample records; check shop reflect changes and that financial/order data is not unexpectedly deleted.
- [ ] Test form validation, admin-only access, CSRF handling, file upload permissions, responsive mobile layout, two-user role isolation, and common failure paths.
- [ ] Confirm SQL Server migrations from earlier updates are applied and the deployed release matches GitHub main.

## Deployment

This review enhancement reuses the existing `Reviews`, `Orders`, `OrderItems`, `Products` and `AdminNotifications` tables. **There is no new EF Core migration in this change.**

```powershell
git switch main
git pull --ff-only origin main
```

If previous migrations have not been applied to your staging/production SQL Server, first back up the database, then use the project's existing Visual Studio Package Manager Console migrations:

```powershell
Update-Database -Context ApplicationDbContext
```

Rebuild/redeploy and clear browser cache as needed. Automated tests are necessary but cannot replace staging SQL Server and real browser checkout/fulfillment acceptance.
