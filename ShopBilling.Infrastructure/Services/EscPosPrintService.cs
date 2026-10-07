using System.Globalization;
using System.Net.Sockets;
using System.Text;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Services;
namespace ShopBilling.Infrastructure.Services;
public class EscPosPrintService : IReceiptPrinter
{
 public byte[] GenerateReceiptBytes(Invoice invoice, string storeName, string storeAddress)
 {
  using var bytes = new MemoryStream();
  void Raw(params byte[] data) => bytes.Write(data);
  void Line(string value) => bytes.Write(Encoding.ASCII.GetBytes(new string(value.Select(c => c >= 32 && c <= 126 ? c : ' ').ToArray()) + "\n"));
  string MoneyText(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
  Raw(0x1B, 0x40, 0x1B, 0x61, 1, 0x1B, 0x45, 1);
  Line(storeName); Raw(0x1B, 0x45, 0); Line(storeAddress);
  Line("--------------------------------");
  Raw(0x1B, 0x61, 0);
  Line(invoice.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
  Line($"Cashier: {invoice.Shift?.Cashier.FullName}");
  Line("Item       Qty   Price     Total");
  foreach (var item in invoice.Items)
  {
   var name = item.Product.Name;
   for (var offset = 0; offset < name.Length; offset += 32) Line(name.Substring(offset, Math.Min(32, name.Length - offset)));
   Line($"{item.Quantity,6} {MoneyText(item.UnitPrice),9} {MoneyText(item.Total),14}");
   if (item.LineDiscount > 0) Line($"  Discount: {MoneyText(item.LineDiscount)}");
  }
  Line("--------------------------------"); Raw(0x1B, 0x61, 2);
  Line($"Subtotal: {MoneyText(invoice.SubTotal)}");
  Line($"Tax: {MoneyText(invoice.TaxTotal)}");
  Line($"Discount: {MoneyText(invoice.DiscountTotal)}");
  Raw(0x1B, 0x45, 1); Line($"TOTAL: {MoneyText(invoice.GrandTotal)}"); Raw(0x1B, 0x45, 0);
  Line($"Payment: {invoice.PaymentType}");
  Line($"Tender: {MoneyText(invoice.PaidAmount)}");
  Line($"Change: {MoneyText(invoice.ChangeAmount)}");
  Raw(0x1B, 0x61, 1); Line(invoice.InvoiceNumber); Line("Thank you for shopping!");
  Raw(0x1B, 0x64, 3, 0x1D, 0x56, 0x41, 0x10);
  return bytes.ToArray();
 }
 public byte[] GenerateDailyZReportBytes(ShopBilling.Core.Models.DailyZReportModel report, string storeName, string storeAddress)
 {
  using var bytes = new MemoryStream();
  void Raw(params byte[] data) => bytes.Write(data);
  void Line(string value) => bytes.Write(Encoding.ASCII.GetBytes(new string(value.Select(c => c >= 32 && c <= 126 ? c : ' ').ToArray()) + "\n"));
  void Amount(string label, decimal value) => Line($"{label,-19}{value.ToString("0.00", CultureInfo.InvariantCulture),13}");
  Raw(0x1B, 0x40, 0x1B, 0x61, 1, 0x1B, 0x45, 1); Line(storeName); Line("OFFICIAL DAILY Z-REPORT"); Raw(0x1B, 0x45, 0);
  Line(storeAddress); Line(report.Date.ToString("yyyy-MM-dd")); Line("--------------------------------"); Raw(0x1B, 0x61, 0);
  Line($"Bills issued: {report.TotalInvoicesCount}"); Amount("Gross sales", report.TotalGrossSales); Amount("Tax collected", report.TotalTaxCollected); Amount("Discounts", report.TotalDiscountsGiven);
  Raw(0x1B, 0x45, 1); Amount("NET SALES", report.NetSales); Raw(0x1B, 0x45, 0);
  Amount("Refunds", report.TotalRefunds); Line($"Return events: {report.TotalReturnsCount}"); Line("--------------------------------");
  Amount("Cash net", report.CashTotal); Amount("Card net", report.CardTotal); Amount("Digital wallet", report.DigitalWalletTotal); Amount("Customer credit", report.CustomerCreditTotal); Amount("Opening floats", report.OpeningFloats); Amount("Shift expected cash", report.ExpectedRegisterCash);
  foreach (var shift in report.Shifts)
  {
   Line($"Shift {shift.Id} {shift.Cashier.FullName} {shift.Status}");
   Amount("Expected", shift.OpeningFloat + shift.CashSalesTotal);
   if (shift.Status == ShopBilling.Core.Enums.ShiftStatus.Closed) { Amount("Counted", shift.ActualClosingCash); Amount("Variance", shift.ActualClosingCash - shift.OpeningFloat - shift.CashSalesTotal); }
  }
  Line("Refunds by processing date."); Line("Gross includes tax before discounts.");
  Raw(0x1B, 0x64, 3, 0x1D, 0x56, 0x41, 0x10); return bytes.ToArray();
 }
 public byte[] GetCashDrawerKickBytes() => [0x1B, 0x70, 0x00, 0x19, 0xFA];
 public async Task PrintToNetworkPrinterAsync(string ipAddress, int port, byte[] payload)
 {
  ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
  if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
  ArgumentNullException.ThrowIfNull(payload);
  using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
  try
  {
   using var client = new TcpClient();
   await client.ConnectAsync(ipAddress, port, timeout.Token);
   await using var stream = client.GetStream();
   await stream.WriteAsync(payload, timeout.Token);
   await stream.FlushAsync(timeout.Token);
  }
  catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
  { throw new IOException($"Printer {ipAddress}:{port} is offline or did not respond. The sale remains saved; reprint its receipt when the printer is available.", ex); }
 }
}
