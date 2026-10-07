using Microsoft.EntityFrameworkCore;
using ShopBilling.Core.Enums;
using ShopBilling.Core.Models;
using ShopBilling.Core.Services;
using ShopBilling.Infrastructure.Data;
namespace ShopBilling.Infrastructure.Services;
public class ReportingService(IDbContextFactory<AppDbContext> factory) : IReportingService
{
 public async Task<DailyZReportModel> GenerateDailyZReportAsync(DateTime date)
 {
  // UI dates represent the store's local calendar day; persisted events use UTC.
  var start = DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();
  var end = DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
  await using var db = await factory.CreateDbContextAsync();
  await using var transaction = await db.Database.BeginTransactionAsync();
  var invoices = await db.Invoices.AsNoTracking().Where(i => i.CreatedAtUtc >= start && i.CreatedAtUtc < end).ToListAsync();
  var refunds = await db.ItemReturns.AsNoTracking().Where(r => r.CreatedAtUtc >= start && r.CreatedAtUtc < end).ToListAsync();
  var shifts = await db.CashierShifts.AsNoTracking().Include(s => s.Cashier).Where(s => s.OpenedAtUtc < end && (s.ClosedAtUtc == null || s.ClosedAtUtc >= start)).OrderBy(s => s.OpenedAtUtc).ToListAsync();
  var shiftIds = shifts.Select(s => s.Id).ToList();
  var shiftInvoices = await db.Invoices.AsNoTracking().Where(i => i.CashierShiftId.HasValue && shiftIds.Contains(i.CashierShiftId.Value) && i.CreatedAtUtc < end).ToListAsync();
  var shiftRefunds = await db.ItemReturns.AsNoTracking().Where(r => r.CashierShiftId.HasValue && shiftIds.Contains(r.CashierShiftId.Value) && r.CreatedAtUtc < end).ToListAsync();
  foreach (var shift in shifts)
  {
   shift.CashSalesTotal = shiftInvoices.Where(i => i.CashierShiftId == shift.Id && i.PaymentType == PaymentMethod.Cash).Sum(i => i.GrandTotal) - shiftRefunds.Where(r => r.CashierShiftId == shift.Id && r.PaymentType == PaymentMethod.Cash).Sum(r => r.RefundAmount);
   shift.CardSalesTotal = shiftInvoices.Where(i => i.CashierShiftId == shift.Id && i.PaymentType == PaymentMethod.Card).Sum(i => i.GrandTotal) - shiftRefunds.Where(r => r.CashierShiftId == shift.Id && r.PaymentType == PaymentMethod.Card).Sum(r => r.RefundAmount);
   if (shift.ClosedAtUtc >= end) { shift.Status = ShiftStatus.Open; shift.ClosedAtUtc = null; shift.ActualClosingCash = 0; }
  }
  decimal Net(PaymentMethod method) => invoices.Where(i => i.PaymentType == method).Sum(i => i.GrandTotal) - refunds.Where(r => r.PaymentType == method).Sum(r => r.RefundAmount);
  var opening = shifts.Sum(s => s.OpeningFloat);
  var report = new DailyZReportModel { Date = date.Date, TotalGrossSales = invoices.Sum(i => i.SubTotal + i.TaxTotal) - refunds.Sum(r => r.GrossAmount + r.TaxAmount), TotalTaxCollected = invoices.Sum(i => i.TaxTotal) - refunds.Sum(r => r.TaxAmount), TotalDiscountsGiven = invoices.Sum(i => i.DiscountTotal) - refunds.Sum(r => r.DiscountAmount), NetSales = invoices.Sum(i => i.GrandTotal) - refunds.Sum(r => r.RefundAmount), TotalInvoicesCount = invoices.Count, CashTotal = Net(PaymentMethod.Cash), CardTotal = Net(PaymentMethod.Card), DigitalWalletTotal = Net(PaymentMethod.DigitalWallet), CustomerCreditTotal = Net(PaymentMethod.CustomerCredit), TotalRefunds = refunds.Sum(r => r.RefundAmount), TotalReturnsCount = refunds.Count, OpeningFloats = opening, ExpectedRegisterCash = shifts.Sum(s => s.OpeningFloat + s.CashSalesTotal), Shifts = shifts };
  await transaction.CommitAsync(); return report;
 }
}
