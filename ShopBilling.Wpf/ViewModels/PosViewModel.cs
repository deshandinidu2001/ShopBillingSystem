using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Enums;
using ShopBilling.Core.Services;
using ShopBilling.Infrastructure.Data;
using ShopBilling.Wpf.Services;
namespace ShopBilling.Wpf.ViewModels;
public partial class PosViewModel : ObservableObject
{
 private readonly IDbContextFactory<AppDbContext> factory;
 private readonly IBillingService billing;
 private readonly IReceiptPrinter printer;
 private readonly TerminalSettings settings;
 private readonly HashSet<CartItemViewModel> observed = [];
 private byte[]? lastReceipt;
 [ObservableProperty] private string barcodeInput = string.Empty;
 public ObservableCollection<CartItemViewModel> CartItems { get; } = [];
 [ObservableProperty] private decimal subTotal;
 [ObservableProperty] private decimal taxTotal;
 [ObservableProperty] private decimal discountTotal;
 [ObservableProperty] private decimal billDiscount;
 [ObservableProperty] private decimal grandTotal;
 [ObservableProperty] private decimal amountTendered;
 [ObservableProperty] private decimal changeDue;
 [ObservableProperty] private string statusMessage = "Ready. Scan a barcode or enter 89010001 to begin.";
 [ObservableProperty] private bool isError;
 [ObservableProperty] private bool isProcessing;
 [ObservableProperty] private CashierShift currentShift = null!;
 [ObservableProperty] private DateTime currentTime = DateTime.Now;
 [ObservableProperty] private decimal actualClosingCash;
 public string StoreName => settings.StoreName;
 public string PrinterStatus => string.IsNullOrWhiteSpace(settings.PrinterAddress) ? "Receipt files enabled • Printer not configured" : $"Printer: {settings.PrinterAddress}:{settings.PrinterPort}";
 public bool CanEdit => !IsProcessing && CurrentShift?.Status == ShiftStatus.Open;
 public PosViewModel(IDbContextFactory<AppDbContext> factory, IBillingService billing, IReceiptPrinter printer, TerminalSettings settings)
 {
  this.factory = factory; this.billing = billing; this.printer = printer; this.settings = settings;
  CartItems.CollectionChanged += OnCartChanged;
 }
 public async Task InitializeAsync()
 {
  await using var db = await factory.CreateDbContextAsync();
  CurrentShift = await db.CashierShifts.AsNoTracking().Include(s => s.Cashier).SingleAsync(s => s.Cashier.Username == "admin" && s.Status == ShiftStatus.Open);
  ActualClosingCash = CurrentShift.OpeningFloat + CurrentShift.CashSalesTotal;
  RefreshCommands();
 }
 public async Task RefreshShiftAsync()
 {
  if (IsProcessing || CurrentShift is null) return;
  await using var db = await factory.CreateDbContextAsync();
  var latest = await db.CashierShifts.AsNoTracking().Include(s => s.Cashier).SingleAsync(s => s.Id == CurrentShift.Id);
  // Preserve counted cash entered by the cashier; expected cash is shown separately in reports.
  CurrentShift = latest;
 }
 private void OnCartChanged(object? sender, NotifyCollectionChangedEventArgs args)
 {
  foreach (var row in observed.Where(r => !CartItems.Contains(r)).ToList()) { row.PropertyChanged -= RowChanged; observed.Remove(row); }
  foreach (var row in CartItems.Where(r => !observed.Contains(r))) { row.PropertyChanged += RowChanged; observed.Add(row); }
  RecalculateTotals();
 }
 private void RowChanged(object? sender, PropertyChangedEventArgs args) => RecalculateTotals();
 partial void OnBillDiscountChanged(decimal value) => RecalculateTotals();
 partial void OnAmountTenderedChanged(decimal value) => ChangeDue = Math.Max(0, value - GrandTotal);
 partial void OnIsProcessingChanged(bool value) => RefreshCommands();
 partial void OnCurrentShiftChanged(CashierShift value) => RefreshCommands();
 private void RefreshCommands()
 {
  OnPropertyChanged(nameof(CanEdit));
  AddBarcodeCommand.NotifyCanExecuteChanged(); PayCashCommand.NotifyCanExecuteChanged(); PayCardCommand.NotifyCanExecuteChanged();
  ClearCartCommand.NotifyCanExecuteChanged(); RemoveItemCommand.NotifyCanExecuteChanged(); UpdateQuantityCommand.NotifyCanExecuteChanged();
  IncreaseQuantityCommand.NotifyCanExecuteChanged(); DecreaseQuantityCommand.NotifyCanExecuteChanged();
  OpenDrawerCommand.NotifyCanExecuteChanged(); ReprintCommand.NotifyCanExecuteChanged(); CloseShiftCommand.NotifyCanExecuteChanged();
 }
 public void RecalculateTotals()
 {
  SubTotal = CartItems.Sum(i => i.UnitPrice * i.Quantity);
  TaxTotal = CartItems.Sum(i => i.TaxAmount);
  DiscountTotal = CartItems.Sum(i => i.LineDiscount) + BillDiscount;
  GrandTotal = Money.Round(SubTotal + TaxTotal - DiscountTotal);
  ChangeDue = Math.Max(0, AmountTendered - GrandTotal);
  RefreshCommands();
 }
 private bool CanPay() => CanEdit && CartItems.Count > 0;
 private bool CanReprint() => !IsProcessing && lastReceipt is not null;
 private bool CanOperate() => !IsProcessing;
 private void Status(string message, bool error = false) { StatusMessage = message; IsError = error; }
 [RelayCommand(CanExecute = nameof(CanEdit))]
 private async Task AddBarcodeAsync()
 {
  var barcode = BarcodeInput.Trim(); if (barcode.Length == 0) return;
  IsProcessing = true;
  try
  {
   await using var db = await factory.CreateDbContextAsync();
   var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Barcode == barcode && p.IsActive);
   if (product is null) throw new InvalidOperationException("No active product matches that barcode.");
   var existing = CartItems.FirstOrDefault(i => i.ProductId == product.Id);
   if ((existing?.Quantity ?? 0) >= product.StockQuantity) throw new InvalidOperationException($"Insufficient stock for {product.Name}.");
   if (existing is null) CartItems.Add(new CartItemViewModel { ProductId = product.Id, Barcode = product.Barcode, ProductName = product.Name, UnitPrice = product.SellingPrice, TaxRatePercent = product.TaxRatePercent });
   else existing.UpdateQuantity(1);
   BarcodeInput = string.Empty; Status($"Added {product.Name}. Stock available: {product.StockQuantity}.");
  }
  catch (Exception ex) { Status(ex.Message, true); }
  finally { IsProcessing = false; }
 }
 [RelayCommand(CanExecute = nameof(CanEdit))]
 private void UpdateQuantity((CartItemViewModel item, int delta) value)
 {
  if (!CartItems.Contains(value.item)) return;
  if (value.item.Quantity + value.delta <= 0) CartItems.Remove(value.item);
  else value.item.UpdateQuantity(value.delta);
 }
 [RelayCommand(CanExecute = nameof(CanEdit))] private void IncreaseQuantity(CartItemViewModel item) => UpdateQuantity((item, 1));
 [RelayCommand(CanExecute = nameof(CanEdit))] private void DecreaseQuantity(CartItemViewModel item) => UpdateQuantity((item, -1));
 [RelayCommand(CanExecute = nameof(CanEdit))] private void RemoveItem(CartItemViewModel item) => CartItems.Remove(item);
 [RelayCommand(CanExecute = nameof(CanPay))] private Task PayCashAsync() => PayAsync(PaymentMethod.Cash);
 [RelayCommand(CanExecute = nameof(CanPay))] private Task PayCardAsync() => PayAsync(PaymentMethod.Card);
 private async Task PayAsync(PaymentMethod method)
 {
  IsProcessing = true;
  Invoice invoice;
  try
  {
   if (method == PaymentMethod.Card) AmountTendered = GrandTotal;
   invoice = await billing.CheckoutAsync(CurrentShift.Id, null, CartItems.Select(i => (i.ProductId, i.Quantity, i.LineDiscount)).ToList(), BillDiscount, method, AmountTendered);
  }
  catch (Exception ex) { Status(ex.Message, true); IsProcessing = false; return; }
  // Once committed, clear the cart even if receipt delivery fails to prevent charging twice.
  ResetCart(); ChangeDue = invoice.ChangeAmount;
  if (method == PaymentMethod.Cash) { CurrentShift.CashSalesTotal += invoice.GrandTotal; ActualClosingCash += invoice.GrandTotal; }
  if (method == PaymentMethod.Card) CurrentShift.CardSalesTotal += invoice.GrandTotal;
  try
  {
   lastReceipt = printer.GenerateReceiptBytes(invoice, settings.StoreName, settings.StoreAddress);
   var receipts = Path.Combine(settings.DataDirectory, "receipts"); Directory.CreateDirectory(receipts);
   await File.WriteAllBytesAsync(Path.Combine(receipts, invoice.InvoiceNumber + ".bin"), lastReceipt);
   if (!string.IsNullOrWhiteSpace(settings.PrinterAddress))
   {
    var payload = method == PaymentMethod.Cash ? lastReceipt.Concat(printer.GetCashDrawerKickBytes()).ToArray() : lastReceipt;
    await printer.PrintToNetworkPrinterAsync(settings.PrinterAddress, settings.PrinterPort, payload);
   }
   Status($"Saved {invoice.InvoiceNumber} • Total {invoice.GrandTotal:C2} • Change {invoice.ChangeAmount:C2}" + (string.IsNullOrWhiteSpace(settings.PrinterAddress) ? " • Receipt stored locally." : " • Receipt sent."));
  }
  catch (Exception ex) { Status($"Sale {invoice.InvoiceNumber} SAVED. Receipt issue: {ex.Message}", true); }
  finally { IsProcessing = false; }
 }
 private void ResetCart() { CartItems.Clear(); BillDiscount = 0; AmountTendered = 0; BarcodeInput = string.Empty; ChangeDue = 0; }
 [RelayCommand(CanExecute = nameof(CanEdit))] private void ClearCart() { ResetCart(); Status("Cart cleared."); }
 [RelayCommand(CanExecute = nameof(CanOperate))]
 private async Task OpenDrawerAsync()
 {
  IsProcessing = true;
  try { RequirePrinter(); await printer.PrintToNetworkPrinterAsync(settings.PrinterAddress, settings.PrinterPort, printer.GetCashDrawerKickBytes()); Status("Cash drawer pulse sent."); }
  catch (Exception ex) { Status(ex.Message, true); } finally { IsProcessing = false; }
 }
 private void RequirePrinter() { if (string.IsNullOrWhiteSpace(settings.PrinterAddress)) throw new InvalidOperationException("Set PrinterAddress in appsettings.json and restart to connect the printer."); }
 [RelayCommand(CanExecute = nameof(CanReprint))]
 private async Task ReprintAsync()
 {
  IsProcessing = true;
  try { RequirePrinter(); await printer.PrintToNetworkPrinterAsync(settings.PrinterAddress, settings.PrinterPort, lastReceipt!); Status("Receipt resent. No sale or drawer pulse was repeated."); }
  catch (Exception ex) { Status(ex.Message, true); } finally { IsProcessing = false; }
 }
 [RelayCommand(CanExecute = nameof(CanEdit))]
 private async Task CloseShiftAsync()
 {
  if (CartItems.Count > 0) { Status("Complete or clear the cart before closing the shift.", true); return; }
  IsProcessing = true;
  try
  {
   CurrentShift = await billing.CloseShiftAsync(CurrentShift.Id, ActualClosingCash);
   var expected = CurrentShift.OpeningFloat + CurrentShift.CashSalesTotal;
   Status($"Shift closed • Expected {expected:C2} • Counted {ActualClosingCash:C2} • Difference {ActualClosingCash - expected:C2}. Restart to open a new shift.");
  }
  catch (Exception ex) { Status(ex.Message, true); } finally { IsProcessing = false; }
 }
}
