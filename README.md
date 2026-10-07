# Shop Billing System

A Windows WPF retail workspace with POS checkout, inventory management, returns and daily reporting, built with C# 12, .NET 8, CommunityToolkit.Mvvm, EF Core SQLite and dependency injection.

## Run

Install the .NET 8 SDK (or a newer SDK that supports targeting .NET 8) and the .NET 8 Windows Desktop Runtime, then run from this directory:

```powershell
dotnet build ShopBillingSystem.sln --configuration Release
dotnet run --project ShopBilling.Wpf --configuration Release
```

Startup creates and seeds the SQLite database and resumes the admin's open shift, or opens one with a $100 float. The requested default users are `admin` and `cashier`, both with password `1234`; the database stores PBKDF2 hashes. This terminal starts directly as Admin as specified; it does not provide a login or role enforcement interface. Change this deployment policy and seed credentials before use in a shared production environment.

Sample barcodes: `89010001` Milk, `89010002` Rice, `89010003` Bread, `89010004` Tea, `89010005` Laundry Soap. Each starts with 100 units. Scan or enter a barcode and press Enter.

## Terminal controls

- F1 focuses the barcode input. USB keyboard-wedge scanners should send a barcode followed by Enter. A rapid alphanumeric scan followed by Enter is also collected when a text field is not focused.
- F5 records a cash sale; tender must cover the total. F6 records a card sale with exact tender.
- F9 sends the cash drawer pulse. F12 clears the current cart.
- The minus/plus and delete buttons edit cart quantities. Line discounts and the bill discount are editable.
- Reprint last receipt sends the latest sale again without repeating payment or drawer opening.
- Enter counted cash and close the shift to reconcile cash and card sales. Clear or finish the cart first. Restart to open the next shift.

Card and wallet methods record payments already accepted externally; no payment terminal or processor integration is supplied. The billing service supports customer credit and digital wallet checkout; this cashier screen exposes cash and card. Customer/user administration, credit repayments, payment processor integration and historical receipt browsing are outside this upgrade. The shell adds product administration, returns and Z-reports.

## Retail workspace

The centralized `Styles/ModernTheme.xaml` provides the slate palette, rounded text fields and buttons, elevated cards, dark grids, compact scrollbars and stock badges. Use the sidebar to switch pages; the menu button collapses it to icons without discarding the cart.

- **POS Checkout:** Existing cash/card checkout and scanner hotkeys. Payment and drawer shortcuts apply on this page.
- **Item & Inventory Catalog:** Filter by barcode or product name, include inactive products, add/edit the catalog, and review low-stock badges. The Adjust action opens the stock panel; enter a signed quantity delta and a required audit reason. All quantity corrections persist `StockAdjustment` history. Stale product forms are rejected if stock changed before saving.
- **Returns & Refunds:** Search an exact invoice number or customer phone. Phone searches list up to 100 recent matching purchases for explicit selection. Select a line, enter the remaining quantity and a reason, then process the refund. Previous returns remain visible and reduce the available quantity.
- **Reports & Shift Closing:** Choose a local business date to see gross revenue, tax, discounts, net sales, refunds and payment totals. Print Official Z-Report saves an ESC/POS summary and dispatches it when the printer is configured. Enter physical counted cash in the register card to close the current shift. The sidebar Close Shift button opens this reconciliation page.

Returns keep original invoices unchanged. An `ItemReturn` records the original payment method, amount, tax/discount reversals, quantity, reason, timestamp and processing shift. Product stock is restored in the same transaction. Refund amounts use original invoice prices, grouped purchased quantities, original line taxes and a proportional allocation of the bill discount. Cumulative rounding ensures that returning every unit refunds the exact original total, including repeated partial returns. The service recalculates and validates the displayed refund before committing.

Cash/card refunds reduce the currently open admin shift's respective payment totals, including returns from older invoices. Credit refunds reduce the customer's outstanding balance; a balance below the refund requires manual reconciliation. The application records the refund; physical cash payout and card/wallet reversals are completed externally. Returned stock is available for resale; damaged/non-resalable goods should receive a separate wastage adjustment with a reason.

Report gross revenue includes tax before discounts and subtracts returned gross/tax. Net sales subtract refunds, while tax and discounts reverse proportionally. Refunds affect the day they are processed, including returns from earlier purchases. Register expectations show opening floats plus cumulative net cash for overlapping shifts through the selected day's end; they are not just the day's cash sales. Report dates follow the Windows terminal's local time zone. Printing the Z-report does not automatically close a shift.

## Money and inventory

All checkout, stock management, refund, shift-close and startup shift writes use explicit atomic SQLite transactions. Each service operation has its own context. Stock, invoices, credit balances and shift totals commit together. Duplicate cart rows cannot bypass stock checks. Cash reconciliation counts sale totals, excluding tendered change, wallet and credit sales.

Prices exclude tax. Each line's tax is rounded to two decimal places, away from zero, after its line discount. The bill discount is applied after line taxes; it cannot exceed the subtotal after line discounts. The receipt, service and UI share this policy. Tax treatment must be configured for the store's jurisdiction before deployment.

The Core project owns entities, enums and service contracts; Infrastructure implements SQLite and ESC/POS; WPF composes services and owns terminal view models and views. Decimal values are stored as SQLite TEXT and aggregate calculations are performed in C#, consistent with [EF Core SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations). Transaction behavior follows [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

## Printer and data setup

Edit `ShopBilling.Wpf/appsettings.json` before building, or the copied `appsettings.json` beside the executable, then restart:

```json
{
  "StoreName": "Corner Store",
  "StoreAddress": "Main Street",
  "PrinterAddress": "192.168.1.100",
  "PrinterPort": 9100
}
```

Use a network ESC/POS printer with a compatible RJ11 drawer connected to it. An empty printer address keeps the application usable without hardware and saves receipt byte streams locally. Output uses a 32-character ASCII receipt format; non-ASCII characters are replaced with spaces. Adapt encoding and width for the installed printer.

The printer connection times out after five seconds. A sale stays committed even if printing fails, and the cart clears to prevent accidental repeat checkout. Receipt `.bin` files are saved before network dispatch. Reprint sends the latest in-session receipt; retained files can be dispatched through `EscPosPrintService` after a restart. TCP delivery confirms bytes were sent, not that paper physically printed; an interrupted transmission can produce a partial receipt.

Default data location: `%LOCALAPPDATA%/ShopBillingSystem/pos_store.db`, with receipts in the adjacent `receipts` directory. `DataDirectory` can be overridden in settings. Back up this directory with the application closed. `EnsureCreatedAsync` initializes a new database; it is not a schema migration system. Startup now runs an idempotent additive upgrade for `StockAdjustments` and `ItemReturns`, preserving the original database tables. Future schema changes still require explicit migrations or a reviewed upgrade procedure.

## Verify

```powershell
dotnet run --project ShopBilling.Verification --configuration Release
```

The verification executable runs 70 checks against isolated temporary SQLite databases. It covers the original checkout flow, additive database upgrades, product editing, audited stock adjustments, stale form protection, cumulative and concurrent returns, proportional refund rounding, credit reversal, daily payment breakdowns, historical local-day boundaries, Z-report byte streams, page commands and shift reconciliation. It renders the POS, inventory, returns and report pages, plus a collapsed sidebar at minimum window size, into `ShopBilling.Verification/bin/Release/net8.0-windows/verification`.

Physical printers, drawers and scanners still require testing on the target hardware. No real payment authorization is performed by verification.
