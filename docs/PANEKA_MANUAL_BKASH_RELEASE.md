# PANENKA — Manual bKash + COD client-release guide

## What this feature does

PANENKA now offers **Cash on Delivery (COD)** or **manual bKash transfer** at new checkout. The manual bKash experience is a guided bank/wallet-transfer instruction and reporting workflow, **not an integration with bKash's hosted checkout or payment API**.

**Do not present customer-entered details as proof of payment.** Staff must independently check the store's bKash wallet or transaction history before confirming receipt. This site never asks for a bKash PIN, password or OTP.

## Customer payment lifecycle

### Cash on Delivery

1. Buyer confirms delivery address and chooses COD.
2. Checkout reserves stock, records a pending cash-on-delivery payment, creates the pending shipment, and clears the bag.
3. Administrators prepare, ship and deliver the parcel through the established order and shipment screens.
4. Only after both order and shipment are **Delivered**, an administrator opens **Admin → Payments → Details**, verifies courier/cash settlement, enters a note and confirms **Cash received**.
5. The payment becomes **Paid**, with verification timestamps, transaction history and order audit entries. Delivery status alone never auto-confirms receipt of cash.

### Manual bKash

1. **Pay with bKash stays enabled whether or not the admin has published a receiving number.** Admin may optionally save an 11-digit receiving number at **Admin → Store settings → Payment methods**. This makes the verified receiving wallet visible in the popup.
2. Buyer reaches **Checkout → Payment**, where **Place COD order** and **Pay with bKash** appear as adjacent active actions. Clicking bKash is always permitted, even when no receiving wallet is configured.
3. Clicking **Pay with bKash** makes an antiforgery-protected request that **reserves the order before showing payment instructions**: the backend validates stock, coupon, delivery district and rate; creates the pending order, reserves stock, saves the recipient number and creates the pending shipment in one database transaction. Double-clicks reuse the same order.
4. The on-page bKash dialog opens after the backend confirms the saved order and exact total. **With a configured merchant wallet**, the popup shows it and Send Money instructions. **Without a configured wallet**, the popup still opens and lets customers report a transfer already made using PANENKA's directly provided receiving details. The site never invents a wallet number and warns customers to ask the store before sending money to an unknown recipient. Customers submit their **sender number** and **transaction ID (TrxID)** (transfer time is pre-filled). Their claim is saved as `Submitted` in the payment ledger. **The order is placed, but the payment stays unpaid and fulfillment is held until staff verification.** Customers can revisit their receipt in My Orders.
5. Staff opens **Admin → Verify bKash**, checks the **actual merchant wallet**, compares recipient, sender, amount and TrxID, records a note, and checks the independent-verification acknowledgement.
6. **Verify** sets payment to **Paid**, records review staff/time and PaidAt, records the transaction-ledger event, advances the order to **Confirmed**, and permits normal fulfillment.
7. **Reject** keeps the order **Pending**, sets payment **Rejected**, logs the reason and allows the buyer to enter corrected details. Previous submission history stays in the ledger.
8. Identical TrxIDs cannot be claimed by another order, including an earlier rejected transaction whose current ID was corrected.

No automatic transfer or refund is performed by PANENKA. The website cannot know if funds arrived without a staff member checking the bKash wallet.

**Production safety:** PANENKA reserves the order and inventory **before** instructing the buyer to transfer money, preventing a sold-out checkout after payment. Closing the popup does not delete the pending order. Customer support must reconcile unverified/abandoned pending bKash orders and avoid asking customers to transfer twice.

### Editing/deleting merchant receiving number

- Edit the saved number and **Save payment settings** to change the recipient for **future** orders.
- Empty the number and save to **stop publishing a merchant receiving number**. This does **not disable Pay with bKash**: customers can still report previously completed transfers using their sender number and TrxID.
- Existing orders retain their original receiving number in the Payment row for accounting. If the number has been retired, the customer page warns against making new transfers to it, but permits reporting a transaction already sent.
- Pending bKash reservations hold inventory until independently reconciled. If the customer abandons checkout, staff may open **Admin → Orders → Details → Unpaid bKash reservation**, confirm that no funds reached the actual merchant wallet, add a reconciliation note, and cancel the order to restore stock and coupon usage. Submitted transfers must first be independently rejected before this action is allowed. A transfer that arrived must be reconciled/refunded instead of cancelled as unpaid.
- bKash manual receipt reporting is available even when COD is disabled or no merchant receiving number has been configured. No gateway call is made.

## Admin Orders ↔ customer payment verification (end-to-end)

**Starting state:** The customer has submitted a sender number, TrxID, and transfer time. In SQL Server `Payments.Status = Submitted`, `Orders.Status = Pending`. This is correct: the order exists, but the wallet transfer is still an unverified claim.

1. Sign in as Admin. Open **Admin → Orders**. A notification at the top counts submitted bKash payments. Each matching order has a **Verify bKash** shortcut. You can also use **Admin → Verify bKash** or **Admin → Payments → Details → Verify payment on this order**.
2. On **Order Details**, the prominent **bKash payment reconciliation** card shows the exact order amount, bKash sender, TrxID, recipient number (if configured), reported transfer time, submitted time, and payment ledger.
3. **Independently inspect the real receiving bKash wallet** and match both amount and TrxID. Enter a meaningful verification note (10–450 characters), check the independent verification acknowledgement, and click **Verify payment & confirm order**.
4. The server uses the same `ManualBkashPaymentService.VerifyAsync` as the central queue. In a single database transaction it writes **Payment = Paid**, `PaidAt`, reviewing admin and review time/note, **Order = Confirmed**, order `PaidAt`, a **Verified** payment transaction, and a **Confirmed** order history entry. Multiple clicks cannot mark it paid again.
5. The admin order reloads, showing **Payment verified** and a new allowed action **Confirmed → Processing**. Staff then advance order to **Processing**, pack the shipment in **Admin → Shipments**, dispatch it, update courier/tracking, and progress through delivery. Customer My Orders and bKash receipt read those same order/payment/shipment rows, automatically refreshing while payment verification is pending.
6. If the real wallet does not show the transfer, enter a reason and click **Reject claim**. Payment becomes **Rejected**, order remains **Pending**, and buyer sees the reason and may correct/resubmit their TrxID. Do **not** dispatch or mark `Paid` based on a customer-submitted reference alone.

**Shipment guard:** A newly created bKash order may not be packed until payment is verified. Already-Packed shipments from older builds remain saved (do not destroy historical courier work), but cannot be dispatched while payment is unverified and order is not Processing.

**No gateway is involved.** This is an intentionally manual bKash receipt/reconciliation workflow, not a bKash API integration. Admin verification is not a payment capture; it only records independently confirmed funds.

### Admin-to-customer acceptance tests

- [ ] Buyer submits bKash payment and sees `Submitted` while order remains `Pending`.
- [ ] Admin Orders highlights submission, and the order page offers **Verify payment & confirm order**, not just a blocked status form.
- [ ] Try verifying without independent confirmation; confirm no payment/order change.
- [ ] Verify with wallet evidence; payment becomes `Paid`, order becomes `Confirmed`, with one Verified ledger entry and one Confirmed status history row.
- [ ] Re-click Verify; confirm no duplicate paid transaction.
- [ ] Customer's own My Orders shows Confirmed and Paid; another customer cannot access the buyer's bKash status.
- [ ] Confirmed → Processing → shipment Packed → Shipped → InTransit → OutForDelivery → Delivered; customer tracking follows.
- [ ] Reject an unverified transfer, see reason customer-side, submit corrected TrxID, then verify it.
- [ ] Check the review queue no longer includes a verified claim.
- [ ] Confirm manual bKash remains available without a published receiving number, but no fake recipient is displayed and staff must check the actual wallet independently.

## Payment storage (SQL Server)

The additive migration `20261120120000_ManualBkashPayments` adds these nullable fields to the existing `Payments` table without dropping or rewriting existing payment or customer records:

- `ManualRecipientNumber`: receiving merchant number snapshot
- `ManualSenderNumber`: customer-reported source wallet
- `TransactionReference`: the TrxID (existing field)
- `ManualClaimedPaidAt`: customer-reported transfer time in UTC
- `ManualSubmittedAt`: server-recorded receipt submission timestamp in UTC
- `ManualReviewedAt`: independent staff review timestamp in UTC
- `ManualReviewedByUserId`: staff reviewer identifier
- `ManualReviewNote`: verification or rejection explanation
- `PaidAt`: when staff **confirmed** that payment was received (existing field)

Every new claim, rejection and verification produces a `PaymentTransactions` record. Administrative payment confirmations record `OrderStatusHistory` entries. A filtered unique SQL index on bKash `TransactionReference` guards against assigning a current TrxID to two orders. Additional application checks include historical rejected claims.

### Production migration

**Back up the SQL Server database first.** The release requires applying the new additive migration, not just copying the web files.

Visual Studio Package Manager Console with the MVC project selected as Default Project:

```powershell
Update-Database -Context ApplicationDbContext
```

Or from a terminal with the correct, reviewed production SQL Server connection string in environment/configuration:

```powershell
dotnet ef database update --project "Jersey-ecommerce/Jersey-ecommerce.csproj" --context ApplicationDbContext
```

**Important:** Review existing migration history and the SQL backup before production execution. The unique TrxID index may be rejected if pre-existing bKash orders contain duplicate non-null transaction IDs. Do not delete or modify client payments to force it through; reconcile any conflicts first.

After migration, start the app and visit **Admin → Store settings → Payment methods**. Publishing the current receiving number is recommended to guide new transfers, but is optional for manual receipt reporting. Verify the bKash button is active in both configured and unconfigured states.

## Client acceptance checklist (staging SQL Server)

- [ ] Save, edit and clear merchant number; **Pay with bKash remains active in every state**. If a number is configured it appears in the popup; if blank, no fake number is displayed and sender/TrxID submission still works.
- [ ] Check exact order total and saved receiving-number snapshot when configured; without a wallet, ensure the order, sender/TrxID and admin verification work with a null recipient.
- [ ] Place a COD order, confirm stock reservation and correct order/customer records.
- [ ] Complete the shipment delivery flow and independently confirm cash received in Admin → Payments.
- [ ] Select from Bangladesh's 64 official districts; test Gazipur / Kaliganj, Dhaka and invalid district rejection. Verify inside/outside Dhaka pricing.
- [ ] Click **Pay with bKash**; verify the backend **reserves one pending order** and reduces inventory once before the modal displays the receiving wallet and amount. Retry the same checkout token without duplicate orders.
- [ ] Confirm the popup shows the saved order number, recipient, accurate amount, copy control and accessible mobile dialog. Enter sender, TrxID and local time, submit, and verify the payment changes from **Pending** to **Submitted** (not Paid).
- [ ] Close the popup before submitting and recover the pending bKash order from My Orders; then complete the transaction report without another payment or order.
- [ ] On a pending abandoned order, confirm a customer cannot cancel or release stock. Admin must provide independent no-funds reconciliation; after that, cancellation restores stock and coupon usage and creates a payment audit event.
- [ ] Attempt to cancel a Submitted bKash claim before rejection and confirm it is blocked. Verify/refund any received funds before stock release.
- [ ] Before order reservation, deliberately use an invalid district, exhausted stock and changed merchant number; verify **no order or stock movement**. After reservation, invalid sender/TrxID/time must not create a claim or mark the order paid.
- [ ] Try another customer's order URL; no payment details should be exposed.
- [ ] Attempt reuse of TrxID on another order; verify rejection.
- [ ] Admin reject a claim; customer sees reason and can submit corrected details without losing ledger history.
- [ ] Admin verify via checked wallet and note; confirm PaidAt, reviewer ID, payment transaction, and order Confirmed.
- [ ] Advance the newly verified order through processing, packing, shipping and delivery.
- [ ] Confirm canceled/returned/failed payment records cannot be silently advanced or marked paid.
- [ ] Test mobile layout (390px), input validation, error messages, copy number, keyboard navigation.
- [ ] Verify backups, migration history, GitHub Release build/tests, and production SQL privileges.

Automated tests exercise SQLite relational flows and the MVC/Razor build. They cannot verify whether a real wallet transfer happened or whether the deployed SQL Server and hosting environment are ready; do not skip staging acceptance.
