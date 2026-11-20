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

1. Administrator saves an 11-digit receiving number at **Admin → Store settings → Payment methods**. Optionally edits the customer instructions.
2. Buyer selects **bKash · Manual transfer**. The order is created with its total, stock reservation, payment status **Pending**, and a snapshot of the merchant receiving number at that moment.
3. Buyer is shown that number and total, performs a transfer via their own bKash application, and submits their **sender number**, **transaction ID (TrxID)** and **actual payment time in Bangladesh time**.
4. PANENKA records the claim, sets payment status **Submitted**, and adds a payment-ledger event. **The order remains Pending/unpaid.** Repeated identical submissions do not create duplicate receipts.
5. Staff opens **Admin → Verify bKash**, checks the **actual merchant wallet**, compares recipient, sender, amount and TrxID, records a note, and checks the independent-verification acknowledgement.
6. **Verify** sets payment to **Paid**, records review staff/time and PaidAt, records the transaction-ledger event, advances the order to **Confirmed**, and permits normal fulfillment.
7. **Reject** keeps the order **Pending**, sets payment **Rejected**, logs the reason and allows the buyer to enter corrected details. Previous submission history stays in the ledger.
8. Identical TrxIDs cannot be claimed by another order, including an earlier rejected transaction whose current ID was corrected.

No automatic transfer or refund is performed by PANENKA. The website cannot know if funds arrived without a staff member checking the bKash wallet.

### Editing/deleting merchant receiving number

- Edit the saved number and **Save payment settings** to change the recipient for **future** orders.
- Empty the number and save to **disable manual bKash for future checkouts**.
- Existing orders retain their original receiving number in the Payment row for accounting. If the number has been retired, the customer page warns against making new transfers to it, but permits reporting a transaction already sent.
- If both bKash and COD are unavailable, checkout explains that no payment method is available.

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

After migration, start the app, visit **Admin → Store settings → Payment methods**, configure the current receiving number, save, and verify it is presented correctly in a new test order.

## Client acceptance checklist (staging SQL Server)

- [ ] Save, edit and clear merchant number; bKash checkout appears only with a valid active 11-digit number.
- [ ] Check exact order total and saved receiving number, including after admin later changes the number.
- [ ] Place a COD order, confirm stock reservation and correct order/customer records.
- [ ] Complete the shipment delivery flow and independently confirm cash received in Admin → Payments.
- [ ] Place a manual bKash order; verify that it is **Pending** immediately after order placement.
- [ ] Submit sender number, TrxID and actual time; verify `Submitted` and that the order cannot dispatch.
- [ ] Try another customer's order URL; no payment details should be exposed.
- [ ] Attempt reuse of TrxID on another order; verify rejection.
- [ ] Admin reject a claim; customer sees reason and can submit corrected details without losing ledger history.
- [ ] Admin verify via checked wallet and note; confirm PaidAt, reviewer ID, payment transaction, and order Confirmed.
- [ ] Advance the newly verified order through processing, packing, shipping and delivery.
- [ ] Confirm canceled/returned/failed payment records cannot be silently advanced or marked paid.
- [ ] Test mobile layout (390px), input validation, error messages, copy number, keyboard navigation.
- [ ] Verify backups, migration history, GitHub Release build/tests, and production SQL privileges.

Automated tests exercise SQLite relational flows and the MVC/Razor build. They cannot verify whether a real wallet transfer happened or whether the deployed SQL Server and hosting environment are ready; do not skip staging acceptance.
