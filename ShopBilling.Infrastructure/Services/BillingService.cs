using Microsoft.EntityFrameworkCore;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Enums;
using ShopBilling.Core.Services;
using ShopBilling.Infrastructure.Data;
namespace ShopBilling.Infrastructure.Services;
public class BillingService(IDbContextFactory<AppDbContext> factory) : IBillingService
{
 public async Task<Invoice> CheckoutAsync(int shiftId, int? customerId, List<(int ProductId, int Qty, decimal LineDiscount)> cart, decimal billDiscount, PaymentMethod paymentMethod, decimal amountTendered)
 {
  if (cart.Count == 0) throw new InvalidOperationException("The cart is empty.");
  if (!Enum.IsDefined(paymentMethod)) throw new ArgumentException("Invalid payment method.");
  if (billDiscount < 0 || amountTendered < 0 || Money.Round(billDiscount) != billDiscount || Money.Round(amountTendered) != amountTendered)
   throw new ArgumentException("Discount and tender must be nonnegative amounts with at most two decimal places.");
  await using var _context = await factory.CreateDbContextAsync();
  await using var transaction = await _context.Database.BeginTransactionAsync();
  var shift = await _context.CashierShifts.Include(s => s.Cashier).SingleOrDefaultAsync(s => s.Id == shiftId);
  if (shift is null || shift.Status != ShiftStatus.Open || !shift.Cashier.IsActive) throw new InvalidOperationException("An active cashier and open shift are required.");
  Customer? customer = null;
  if (customerId.HasValue) customer = await _context.Customers.FindAsync(customerId.Value) ?? throw new InvalidOperationException("Customer was not found.");
  var invoice = new Invoice { Shift = shift, Customer = customer, PaymentType = paymentMethod };
  foreach (var entry in cart)
  {
   var product = await _context.Products.FindAsync(entry.ProductId) ?? throw new InvalidOperationException("Product was not found.");
   if (!product.IsActive) throw new InvalidOperationException($"{product.Name} is inactive.");
   if (entry.Qty <= 0 || entry.LineDiscount < 0 || Money.Round(entry.LineDiscount) != entry.LineDiscount) throw new ArgumentException("Quantity must be positive and line discount must be a valid monetary amount.");
   if (product.SellingPrice < 0 || product.TaxRatePercent < 0 || product.TaxRatePercent > 100) throw new InvalidOperationException("Invalid product price or tax rate.");
   if (product.StockQuantity < entry.Qty) throw new InvalidOperationException($"Insufficient stock for {product.Name}. Available: {product.StockQuantity}.");
   var gross = Money.Round(product.SellingPrice * entry.Qty);
   if (entry.LineDiscount > gross) throw new ArgumentException("Line discount cannot exceed the line price.");
   product.StockQuantity -= entry.Qty;
   invoice.Items.Add(new InvoiceItem { Product = product, Quantity = entry.Qty, UnitPrice = product.SellingPrice, LineDiscount = entry.LineDiscount, TaxRatePercent = product.TaxRatePercent });
  }
  invoice.SubTotal = invoice.Items.Sum(i => i.Quantity * i.UnitPrice);
  invoice.TaxTotal = invoice.Items.Sum(i => Money.Round(i.Total * i.TaxRatePercent / 100m));
  invoice.DiscountTotal = invoice.Items.Sum(i => i.LineDiscount) + billDiscount;
  var afterLineDiscount = invoice.Items.Sum(i => i.Total);
  if (billDiscount > afterLineDiscount) throw new ArgumentException("Bill discount cannot exceed the discounted subtotal.");
  // Bill discount is applied after per-line tax; the receipt and UI use the same policy.
  invoice.GrandTotal = Money.Round(invoice.SubTotal + invoice.TaxTotal - invoice.DiscountTotal);
  if (paymentMethod == PaymentMethod.CustomerCredit)
  {
   if (customer is null) throw new InvalidOperationException("Select a customer for credit sales.");
   if (amountTendered != 0) throw new ArgumentException("Credit sales require zero tender; split payments are not supported.");
   if (customer.OutstandingBalance + invoice.GrandTotal > customer.CreditLimit) throw new InvalidOperationException("Customer credit limit exceeded.");
   customer.OutstandingBalance += invoice.GrandTotal;
  }
  else
  {
   if (amountTendered < invoice.GrandTotal) throw new InvalidOperationException("Tender is below the amount due.");
   if (paymentMethod != PaymentMethod.Cash && amountTendered != invoice.GrandTotal) throw new InvalidOperationException("Noncash tender must equal the amount due.");
   invoice.PaidAmount = amountTendered;
   invoice.ChangeAmount = paymentMethod == PaymentMethod.Cash ? amountTendered - invoice.GrandTotal : 0;
  }
  var prefix = $"INV-{invoice.CreatedAtUtc:yyyyMMdd}-";
  var last = await _context.Invoices.Where(i => i.InvoiceNumber.StartsWith(prefix)).OrderByDescending(i => i.Id).Select(i => i.InvoiceNumber).FirstOrDefaultAsync();
  var sequence = last is null ? 1 : int.Parse(last[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture) + 1;
  invoice.InvoiceNumber = prefix + sequence.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
  if (paymentMethod == PaymentMethod.Cash) shift.CashSalesTotal += invoice.GrandTotal;
  if (paymentMethod == PaymentMethod.Card) shift.CardSalesTotal += invoice.GrandTotal;
  _context.Invoices.Add(invoice);
  await _context.SaveChangesAsync();
  await transaction.CommitAsync();
  return invoice;
 }
 public async Task<CashierShift> CloseShiftAsync(int shiftId, decimal actualCash)
 {
  if (actualCash < 0 || Money.Round(actualCash) != actualCash) throw new ArgumentException("Closing cash must be a valid nonnegative monetary amount.");
  await using var _context = await factory.CreateDbContextAsync();
  await using var transaction = await _context.Database.BeginTransactionAsync();
  var shift = await _context.CashierShifts.Include(s => s.Cashier).SingleOrDefaultAsync(s => s.Id == shiftId) ?? throw new InvalidOperationException("Shift was not found.");
  if (shift.Status != ShiftStatus.Open) throw new InvalidOperationException("Shift is already closed.");
  var invoices = await _context.Invoices.Where(i => i.CashierShiftId == shiftId).ToListAsync();
  var refunds = await _context.ItemReturns.Where(r => r.CashierShiftId == shiftId).ToListAsync();
  shift.CashSalesTotal = invoices.Where(i => i.PaymentType == PaymentMethod.Cash).Sum(i => i.GrandTotal) - refunds.Where(r => r.PaymentType == PaymentMethod.Cash).Sum(r => r.RefundAmount);
  shift.CardSalesTotal = invoices.Where(i => i.PaymentType == PaymentMethod.Card).Sum(i => i.GrandTotal) - refunds.Where(r => r.PaymentType == PaymentMethod.Card).Sum(r => r.RefundAmount);
  shift.ActualClosingCash = actualCash;
  shift.Status = ShiftStatus.Closed;
  shift.ClosedAtUtc = DateTime.UtcNow;
  await _context.SaveChangesAsync();
  await transaction.CommitAsync();
  return shift;
 }
}
