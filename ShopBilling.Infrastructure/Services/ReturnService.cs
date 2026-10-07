using Microsoft.EntityFrameworkCore;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Enums;
using ShopBilling.Core.Models;
using ShopBilling.Core.Services;
using ShopBilling.Infrastructure.Data;
namespace ShopBilling.Infrastructure.Services;
public class ReturnService(IDbContextFactory<AppDbContext> factory) : IReturnService
{
 public async Task<Invoice?> FindInvoiceForReturnAsync(string invoiceNumber)
 {
  await using var db = await factory.CreateDbContextAsync();
  return await db.Invoices.AsNoTracking().Include(i => i.Items).ThenInclude(i => i.Product).Include(i => i.Customer).Include(i => i.Shift).ThenInclude(s => s!.Cashier).SingleOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber.Trim());
 }
 public async Task<List<Invoice>> SearchInvoicesAsync(string query)
 {
  if (string.IsNullOrWhiteSpace(query)) return [];
  var term = query.Trim();
  await using var db = await factory.CreateDbContextAsync();
  return await db.Invoices.AsNoTracking().Include(i => i.Customer).Where(i => i.InvoiceNumber == term || (i.Customer != null && i.Customer.PhoneNumber == term)).OrderByDescending(i => i.CreatedAtUtc).Take(100).ToListAsync();
 }
 public async Task<List<ReturnLineModel>> GetReturnLinesAsync(int invoiceId)
 {
  await using var db = await factory.CreateDbContextAsync();
  var invoice = await db.Invoices.AsNoTracking().Include(i => i.Items).ThenInclude(i => i.Product).SingleOrDefaultAsync(i => i.Id == invoiceId) ?? throw new InvalidOperationException("Invoice was not found.");
  var returns = await db.ItemReturns.AsNoTracking().Where(r => r.InvoiceId == invoiceId).ToListAsync();
  return BuildLines(invoice, returns);
 }
 private static List<ReturnLineModel> BuildLines(Invoice invoice, List<ItemReturn> returns)
 {
  var groups = invoice.Items.GroupBy(i => i.ProductId).OrderBy(g => g.Key).ToList();
  var billDiscount = invoice.DiscountTotal - invoice.Items.Sum(i => i.LineDiscount);
  var netSubtotal = invoice.Items.Sum(i => i.Total);
  decimal cumulative = 0, allocated = 0;
  var result = new List<ReturnLineModel>();
  foreach (var group in groups)
  {
   var gross = group.Sum(i => i.Quantity * i.UnitPrice);
   var lineDiscount = group.Sum(i => i.LineDiscount);
   var tax = group.Sum(i => Money.Round(i.Total * i.TaxRatePercent / 100m));
   cumulative += gross - lineDiscount;
   var nextAllocated = netSubtotal == 0 ? 0 : Money.Round(billDiscount * cumulative / netSubtotal);
   var share = nextAllocated - allocated; allocated = nextAllocated;
   result.Add(new ReturnLineModel { ProductId = group.Key, ProductName = group.First().Product.Name, PurchasedQuantity = group.Sum(i => i.Quantity), ReturnedQuantity = returns.Where(r => r.ProductId == group.Key).Sum(r => r.Quantity), GrossTotal = gross, TaxTotal = tax, RefundableTotal = gross - lineDiscount - share + tax });
  }
  return result;
 }
 public async Task<bool> ProcessItemReturnAsync(int invoiceId, int productId, int returnQty, decimal refundAmount, string reason)
 {
  if (returnQty <= 0 || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new ArgumentException("Provide a positive return quantity and reason (up to 500 characters).");
  await using var _context = await factory.CreateDbContextAsync();
  await using var transaction = await _context.Database.BeginTransactionAsync();
  var invoice = await _context.Invoices.Include(i => i.Items).ThenInclude(i => i.Product).Include(i => i.Customer).SingleOrDefaultAsync(i => i.Id == invoiceId) ?? throw new InvalidOperationException("Invoice was not found.");
  var previous = await _context.ItemReturns.Where(r => r.InvoiceId == invoiceId).ToListAsync();
  var line = BuildLines(invoice, previous).SingleOrDefault(l => l.ProductId == productId) ?? throw new InvalidOperationException("Product is not on this invoice.");
  var expected = line.RefundFor(returnQty);
  if (refundAmount != expected) throw new InvalidOperationException($"Refund amount changed or is invalid. Expected {expected:0.00}; reload the invoice.");
  var product = invoice.Items.First(i => i.ProductId == productId).Product;
  product.StockQuantity = checked(product.StockQuantity + returnQty);
  // This terminal operates as admin. Refund payouts belong to its current shift, even for an older invoice.
  var shift = await _context.CashierShifts.Include(s => s.Cashier).SingleOrDefaultAsync(s => s.Cashier.Username == "admin" && s.Status == ShiftStatus.Open);
  if (shift is null) throw new InvalidOperationException("Open the terminal shift before processing a return.");
  if (invoice.PaymentType == PaymentMethod.CustomerCredit)
  {
   if (invoice.Customer is null || invoice.Customer.OutstandingBalance < expected) throw new InvalidOperationException("Credit balance requires manual reconciliation before this refund.");
   invoice.Customer.OutstandingBalance -= expected;
  }
  if (invoice.PaymentType == PaymentMethod.Cash) shift.CashSalesTotal -= expected;
  if (invoice.PaymentType == PaymentMethod.Card) shift.CardSalesTotal -= expected;
  var gross = line.GrossFor(returnQty); var tax = line.TaxFor(returnQty);
  _context.ItemReturns.Add(new ItemReturn { InvoiceId = invoiceId, ProductId = productId, CashierShiftId = shift.Id, Quantity = returnQty, RefundAmount = expected, GrossAmount = gross, TaxAmount = tax, DiscountAmount = gross + tax - expected, PaymentType = invoice.PaymentType, Reason = reason.Trim() });
  _context.StockAdjustments.Add(new StockAdjustment { ProductId = productId, QuantityChange = returnQty, StockAfter = product.StockQuantity, Reason = $"Return {invoice.InvoiceNumber}: {reason.Trim()}" });
  await _context.SaveChangesAsync(); await transaction.CommitAsync(); return true;
 }
}
