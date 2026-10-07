using Microsoft.EntityFrameworkCore;
namespace ShopBilling.Infrastructure.Data;
public static class DatabaseInitializer
{
 public static async Task InitializeAsync(AppDbContext db)
 {
  await db.Database.EnsureCreatedAsync();
  // Additive upgrade for the original POS database. Existing invoice and product tables remain intact.
  await using var transaction = await db.Database.BeginTransactionAsync();
  await db.Database.ExecuteSqlRawAsync("""
   CREATE TABLE IF NOT EXISTS "StockAdjustments" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StockAdjustments" PRIMARY KEY AUTOINCREMENT,
    "ProductId" INTEGER NOT NULL, "QuantityChange" INTEGER NOT NULL, "StockAfter" INTEGER NOT NULL,
    "Reason" TEXT NOT NULL, "CreatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "FK_StockAdjustments_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT);
   CREATE INDEX IF NOT EXISTS "IX_StockAdjustments_ProductId" ON "StockAdjustments" ("ProductId");
   CREATE TABLE IF NOT EXISTS "ItemReturns" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemReturns" PRIMARY KEY AUTOINCREMENT,
    "InvoiceId" INTEGER NOT NULL, "ProductId" INTEGER NOT NULL, "CashierShiftId" INTEGER NULL,
    "Quantity" INTEGER NOT NULL, "RefundAmount" TEXT NOT NULL, "GrossAmount" TEXT NOT NULL,
    "TaxAmount" TEXT NOT NULL, "DiscountAmount" TEXT NOT NULL, "PaymentType" INTEGER NOT NULL,
    "Reason" TEXT NOT NULL, "CreatedAtUtc" TEXT NOT NULL,
    CONSTRAINT "FK_ItemReturns_Invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES "Invoices" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ItemReturns_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ItemReturns_CashierShifts_CashierShiftId" FOREIGN KEY ("CashierShiftId") REFERENCES "CashierShifts" ("Id") ON DELETE RESTRICT);
   CREATE INDEX IF NOT EXISTS "IX_ItemReturns_InvoiceId_ProductId" ON "ItemReturns" ("InvoiceId", "ProductId");
   CREATE INDEX IF NOT EXISTS "IX_ItemReturns_ProductId" ON "ItemReturns" ("ProductId");
   CREATE INDEX IF NOT EXISTS "IX_ItemReturns_CashierShiftId" ON "ItemReturns" ("CashierShiftId");
   CREATE INDEX IF NOT EXISTS "IX_ItemReturns_CreatedAtUtc" ON "ItemReturns" ("CreatedAtUtc");
   """);
  await transaction.CommitAsync();
 }
}
