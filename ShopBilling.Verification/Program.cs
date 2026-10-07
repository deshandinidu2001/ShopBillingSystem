using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Enums;
using ShopBilling.Infrastructure.Data;
using ShopBilling.Infrastructure.Services;
using ShopBilling.Wpf.Services;
using ShopBilling.Wpf.ViewModels;
using ShopBilling.Wpf.ViewModels.Pages;
using ShopBilling.Wpf.Views.Pages;
using ShopBilling.Wpf.Views;

internal class Program
{
 private static int checks;
 [STAThread]
 public static int Main()
 {
  var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
  app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/ShopBilling.Wpf;component/Styles/ModernTheme.xaml", UriKind.Relative) });
  foreach (var (vm, view) in new[] { (typeof(PosViewModel), typeof(PosView)), (typeof(InventoryViewModel), typeof(InventoryPage)), (typeof(ReturnsViewModel), typeof(ReturnsPage)), (typeof(ReportsViewModel), typeof(ReportsPage)) })
   app.Resources.Add(new DataTemplateKey(vm), new DataTemplate(vm) { VisualTree = new FrameworkElementFactory(view) });
  int result = 0;
  app.Startup += async (_, _) =>
  {
   try { await Verify(); await VerifyRetail(); Console.WriteLine($"PASS: {checks} checks."); }
   catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
   finally { app.Shutdown(); }
  };
  app.Run(); return result;
 }
 private static void Check(bool condition, string name)
 {
  if (!condition) throw new InvalidOperationException("FAILED: " + name);
  checks++; Console.WriteLine("PASS: " + name);
 }
 private static async Task Reject(Func<Task> action, string name)
 {
  try { await action(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbUpdateException or IOException) { Check(true, name); return; }
  throw new InvalidOperationException("FAILED: expected rejection: " + name);
 }
 private static async Task Verify()
 {
  var directory = Path.Combine(Path.GetTempPath(), "ShopBillingVerify-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
  IDbContextFactory<AppDbContext> factory = new Factory(Path.Combine(directory, "test.db"));
  int shiftId;
  await using (var db = await factory.CreateDbContextAsync())
  {
   await db.Database.EnsureCreatedAsync();
   Check(await db.Products.CountAsync() == 5 && await db.Users.CountAsync() == 2, "seed products and users");
   var admin = await db.Users.SingleAsync(x => x.Username == "admin");
   Check(PasswordHasher.Verify("1234", admin.PasswordHash) && !PasswordHasher.Verify("wrong", admin.PasswordHash), "hashed seed credentials");
   await using var tx = await db.Database.BeginTransactionAsync();
   var shift = new CashierShift { CashierId = admin.Id, OpeningFloat = 100 };
   db.CashierShifts.Add(shift); db.Customers.Add(new Customer { Id = 2, Name = "Credit Customer", PhoneNumber = "0771234567", CreditLimit = 10 });
   await db.SaveChangesAsync(); await tx.CommitAsync(); shiftId = shift.Id;
  }
  var billing = new BillingService(factory); var printer = new EscPosPrintService();
  var cash = await billing.CheckoutAsync(shiftId, null, [(1, 2, 0.20m)], 0.10m, PaymentMethod.Cash, 5m);
  Check(cash.SubTotal == 3.60m && cash.TaxTotal == 0.17m && cash.DiscountTotal == 0.30m && cash.GrandTotal == 3.47m && cash.ChangeAmount == 1.53m, "cash totals, discounts, tax and change");
  Check(cash.Items[0].Product.Name == "Milk 1 L" && cash.Shift?.Cashier.Username == "admin", "returned navigation properties");
  await Reject(() => billing.CheckoutAsync(shiftId, null, [(1, 1, 0), (2, 101, 0)], 0, PaymentMethod.Cash, 1000), "late stock failure rejects entire sale");
  await using (var db = await factory.CreateDbContextAsync()) Check((await db.Products.FindAsync(1))!.StockQuantity == 98 && await db.Invoices.CountAsync() == 1, "rollback leaves stock and invoices unchanged");
  await Reject(() => billing.CheckoutAsync(shiftId, null, [(1, 99, 0), (1, 2, 0)], 0, PaymentMethod.Cash, 1000), "duplicate product rows cannot oversell");
  await Reject(() => billing.CheckoutAsync(shiftId, null, [(1, 0, 0)], 0, PaymentMethod.Cash, 10), "zero quantity rejected");
  await Reject(() => billing.CheckoutAsync(shiftId, null, [(1, 1, 2)], 0, PaymentMethod.Cash, 10), "excess line discount rejected");
  await Reject(() => billing.CheckoutAsync(shiftId, null, [(1, 1, 0)], 2, PaymentMethod.Cash, 10), "excess bill discount rejected");
  await Reject(() => billing.CheckoutAsync(shiftId, null, [(1, 1, 0)], 0, PaymentMethod.Cash, 1), "insufficient tender rejected");
  await Reject(() => billing.CheckoutAsync(shiftId, null, [(1, 1, 0)], 0, PaymentMethod.CustomerCredit, 0), "credit requires customer");
  await Reject(() => billing.CheckoutAsync(shiftId, 1, [(1, 1, 0)], 0, PaymentMethod.CustomerCredit, 0), "walk-in credit rejected");
  var credit = await billing.CheckoutAsync(shiftId, 2, [(2, 2, 0)], 0, PaymentMethod.CustomerCredit, 0);
  Check(credit.GrandTotal == 4.40m && credit.Customer!.OutstandingBalance == 4.40m && credit.PaidAmount == 0, "credit balance increment");
  await Reject(() => billing.CheckoutAsync(shiftId, 2, [(2, 3, 0)], 0, PaymentMethod.CustomerCredit, 0), "credit limit rejection");
  var card = await billing.CheckoutAsync(shiftId, null, [(5, 1, 0)], 0, PaymentMethod.Card, 1.10m);
  Check(card.ChangeAmount == 0 && card.InvoiceNumber.EndsWith("003"), "card payment and sequential invoice numbers");
  var wallet = await billing.CheckoutAsync(shiftId, null, [(5, 1, 0)], 0, PaymentMethod.DigitalWallet, 1.10m);
  Check(wallet.PaidAmount == wallet.GrandTotal, "digital wallet payment");
  var receipt = printer.GenerateReceiptBytes(cash, "Test Store", "Test Address");
  Check(receipt.Take(2).SequenceEqual(new byte[] { 0x1B, 0x40 }) && receipt.TakeLast(4).SequenceEqual(new byte[] { 0x1D, 0x56, 0x41, 0x10 }) && Encoding.ASCII.GetString(receipt).Contains(cash.InvoiceNumber), "ESC/POS receipt initialization, invoice and cut");
  Check(printer.GetCashDrawerKickBytes().SequenceEqual(new byte[] { 0x1B, 0x70, 0, 0x19, 0xFA }), "RJ11 cash drawer pulse");
  var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
  var accept = listener.AcceptTcpClientAsync();
  await printer.PrintToNetworkPrinterAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, receipt);
  using (var client = await accept) { using var output = new MemoryStream(); await client.GetStream().CopyToAsync(output); Check(output.ToArray().SequenceEqual(receipt), "network printer receives exact byte stream"); }
  var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
  await Reject(() => printer.PrintToNetworkPrinterAsync("127.0.0.1", port, receipt), "offline printer error");
  var settings = new TerminalSettings { StoreName = "Corner Store", DataDirectory = directory };
  var vm = new PosViewModel(factory, billing, printer, settings); await vm.InitializeAsync();
  vm.BarcodeInput = "89010001"; await vm.AddBarcodeCommand.ExecuteAsync(null);
  vm.BarcodeInput = "89010001"; await vm.AddBarcodeCommand.ExecuteAsync(null);
  Check(vm.CartItems.Count == 1 && vm.CartItems[0].Quantity == 2 && vm.GrandTotal == 3.78m, "barcode command merges rows and recalculates");
  vm.CartItems[0].LineDiscount = 0.20m; vm.BillDiscount = 0.10m;
  Check(vm.GrandTotal == 3.47m, "editable discounts match billing totals");
  vm.AmountTendered = 5;
  Check(vm.ChangeDue == 1.53m, "observable change due");
  var window = new MainWindow(vm) { ShowActivated = false, Left = -10000, Top = -10000 };
  window.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
  var outputDirectory = Path.Combine(AppContext.BaseDirectory, "verification"); Directory.CreateDirectory(outputDirectory);
  Render(window, Path.Combine(outputDirectory, "pos-preview.png"));
  window.Width = 1100; window.Height = 790; await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
  Render(window, Path.Combine(outputDirectory, "pos-minimum-preview.png"));
  Check(((PosView)window.Content).FindName("BarcodeBox") is System.Windows.Controls.TextBox && ((PosView)window.Content).FindName("CartGrid") is System.Windows.Controls.DataGrid, "WPF window loads and renders at default and minimum sizes");
  window.Hide();
  vm.UpdateQuantityCommand.Execute((vm.CartItems[0], -1)); Check(vm.CartItems[0].Quantity == 1, "quantity decrease command");
  vm.ClearCartCommand.Execute(null); Check(vm.GrandTotal == 0 && vm.CartItems.Count == 0, "clear cart resets totals");
  vm.BarcodeInput = "89010005"; await vm.AddBarcodeCommand.ExecuteAsync(null); vm.AmountTendered = 2;
  settings.PrinterAddress = "127.0.0.1"; settings.PrinterPort = port;
  await vm.PayCashCommand.ExecuteAsync(null);
  Check(vm.CartItems.Count == 0 && vm.IsError && vm.StatusMessage.Contains("SAVED") && vm.ChangeDue == 0.90m, "printer failure preserves sale and clears cart");
  Check(Directory.GetFiles(Path.Combine(directory, "receipts"), "*.bin").Length == 1, "receipt persisted for reprinting");
  var closed = await billing.CloseShiftAsync(shiftId, 104.57m);
  Check(closed.Status == ShiftStatus.Closed && closed.CashSalesTotal == 4.57m && closed.CardSalesTotal == 1.10m && closed.ActualClosingCash == 104.57m, "shift close reconciles net cash and card totals");
  await Reject(() => billing.CheckoutAsync(shiftId, null, [(1, 1, 0)], 0, PaymentMethod.Cash, 10), "closed shift rejects checkout");
  await Reject(() => billing.CloseShiftAsync(shiftId, 100), "closed shift cannot close again");
  Console.WriteLine("Preview directory: " + outputDirectory);
  // Test databases are isolated in the OS temporary folder and never touch the live store.
 }
 private static async Task VerifyRetail()
 {
  var directory = Path.Combine(Path.GetTempPath(), "ShopBillingRetailVerify-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
  IDbContextFactory<AppDbContext> factory = new Factory(Path.Combine(directory, "retail.db"));
  int shiftId;
  await using (var db = await factory.CreateDbContextAsync())
  {
   await DatabaseInitializer.InitializeAsync(db);
   // Simulate upgrading the original schema with existing products and users.
   await db.Database.ExecuteSqlRawAsync("DROP TABLE ItemReturns; DROP TABLE StockAdjustments;");
   await DatabaseInitializer.InitializeAsync(db); await DatabaseInitializer.InitializeAsync(db);
   Check(await db.Products.CountAsync() == 5 && await db.ItemReturns.CountAsync() == 0, "original database additive upgrade is idempotent and preserves data");
   await using var tx = await db.Database.BeginTransactionAsync();
   var shift = new CashierShift { CashierId = 1, OpeningFloat = 100 }; db.CashierShifts.Add(shift);
   db.Customers.Add(new Customer { Id = 2, Name = "Asha", PhoneNumber = "0771122334", CreditLimit = 100 });
   await db.SaveChangesAsync(); await tx.CommitAsync(); shiftId = shift.Id;
  }
  var inventory = new InventoryService(factory); var returns = new ReturnService(factory); var reporting = new ReportingService(factory); var billing = new BillingService(factory); var printer = new EscPosPrintService();
  Check((await inventory.GetAllProductsAsync("MILK")).Count == 1 && (await inventory.GetAllProductsAsync("8901000")).Count == 5, "catalog filters by case-insensitive name or barcode");
  var added = await inventory.CreateOrUpdateProductAsync(new Product { Barcode = "TEST100", Name = "Cereal", CostPrice = 2, SellingPrice = 3, StockQuantity = 5, LowStockThreshold = 5 });
  Check(added.Id > 5 && (await inventory.GetLowStockAlertsAsync()).Any(p => p.Id == added.Id), "product creation and low-stock boundary alert");
  await Reject(() => inventory.CreateOrUpdateProductAsync(new Product { Barcode = "TEST100", Name = "Duplicate" }), "duplicate barcode rejected");
  await Reject(() => inventory.CreateOrUpdateProductAsync(new Product { Barcode = "INVALID", Name = "Invalid", TaxRatePercent = 101 }), "invalid product tax rejected");
  await inventory.AdjustStockAsync(added.Id, 4, "Supplier delivery");
  await Reject(() => inventory.AdjustStockAsync(added.Id, -10, "Wastage"), "stock adjustment cannot create negative stock");
  await Reject(() => inventory.AdjustStockAsync(added.Id, 1, ""), "stock adjustment requires audit reason");
  added.ExpectedStockQuantity = 5; added.StockQuantity = 5;
  await Reject(() => inventory.CreateOrUpdateProductAsync(added), "stale product form cannot overwrite updated stock");
  added = (await inventory.GetAllProductsAsync("TEST100"))[0]; added.Name = "Cereal 500 g"; added.IsActive = false;
  await inventory.CreateOrUpdateProductAsync(added);
  Check((await inventory.GetAllProductsAsync("TEST100")).Count == 0 && (await inventory.GetAllProductsAsync("TEST100", true)).Count == 1, "product edit and inactive filtering");
  await using (var db = await factory.CreateDbContextAsync()) Check(await db.StockAdjustments.CountAsync() == 2 && (await db.Products.FindAsync(added.Id))!.StockQuantity == 9, "stock creation and delivery persist audit entries");
  var sale = await billing.CheckoutAsync(shiftId, null, [(1, 3, 0.30m), (1, 1, 0.10m), (2, 2, 0)], 0.51m, PaymentMethod.Cash, 20);
  var lines = await returns.GetReturnLinesAsync(sale.Id);
  Check(lines.Count == 2 && lines.Sum(l => l.RefundableTotal) == sale.GrandTotal && sale.GrandTotal == 11.04m, "duplicate purchase lines group and allocate bill discount exactly");
  Check((await returns.FindInvoiceForReturnAsync(sale.InvoiceNumber))!.Items.Count == 3, "return search loads purchase details");
  await Reject(() => returns.ProcessItemReturnAsync(sale.Id, 1, 5, 0, "Too many"), "cannot return more than purchased");
  await Reject(() => returns.ProcessItemReturnAsync(sale.Id, 1, 1, 99, "Wrong amount"), "caller cannot inflate refund amount");
  await Reject(() => returns.ProcessItemReturnAsync(sale.Id, 1, 1, lines[0].RefundFor(1), ""), "refund requires reason");
  var firstRefund = lines.Single(l => l.ProductId == 1).RefundFor(1);
  await returns.ProcessItemReturnAsync(sale.Id, 1, 1, firstRefund, "Damaged packaging");
  lines = await returns.GetReturnLinesAsync(sale.Id);
  Check(lines.Single(l => l.ProductId == 1).AvailableQuantity == 3, "partial return persists remaining quantity");
  await returns.ProcessItemReturnAsync(sale.Id, 1, 3, lines.Single(l => l.ProductId == 1).RefundFor(3), "Remaining pack return");
  await Reject(() => returns.ProcessItemReturnAsync(sale.Id, 1, 1, firstRefund, "Repeated"), "repeat returns cannot exceed cumulative purchased quantity");
  lines = await returns.GetReturnLinesAsync(sale.Id);
  await returns.ProcessItemReturnAsync(sale.Id, 2, 2, lines.Single(l => l.ProductId == 2).RefundFor(2), "Rice returned");
  await using (var db = await factory.CreateDbContextAsync())
  {
   var records = await db.ItemReturns.Where(r => r.InvoiceId == sale.Id).ToListAsync();
   Check(records.Sum(r => r.RefundAmount) == sale.GrandTotal && records.Sum(r => r.TaxAmount) == sale.TaxTotal && records.Sum(r => r.DiscountAmount) == sale.DiscountTotal, "full refund exactly reverses price, tax and discounts");
   Check((await db.Products.FindAsync(1))!.StockQuantity == 100 && (await db.Products.FindAsync(2))!.StockQuantity == 100 && (await db.Invoices.FindAsync(sale.Id))!.GrandTotal == 11.04m, "returns restore inventory without modifying original invoice");
  }
  var roundingSale = await billing.CheckoutAsync(shiftId, null, [(5, 3, 0)], 0.01m, PaymentMethod.Card, 3.29m);
  for (var count = 0; count < 3; count++)
  {
   var line = (await returns.GetReturnLinesAsync(roundingSale.Id)).Single();
   await returns.ProcessItemReturnAsync(roundingSale.Id, 5, 1, line.RefundFor(1), "Single unit return");
  }
  await using (var db = await factory.CreateDbContextAsync()) Check((await db.ItemReturns.Where(r => r.InvoiceId == roundingSale.Id).ToListAsync()).Sum(r => r.RefundAmount) == 3.29m, "fractional unit refunds reconcile without rounding drift");
  var credit = await billing.CheckoutAsync(shiftId, 2, [(2, 2, 0)], 0, PaymentMethod.CustomerCredit, 0);
  Check((await returns.SearchInvoicesAsync("0771122334")).Count == 1, "customer phone searches purchases");
  await returns.ProcessItemReturnAsync(credit.Id, 2, 1, 2.20m, "Credit return");
  await using (var db = await factory.CreateDbContextAsync()) Check((await db.Customers.FindAsync(2))!.OutstandingBalance == 2.20m, "credit refund reduces customer debt");
  var wallet = await billing.CheckoutAsync(shiftId, null, [(5, 1, 0)], 0, PaymentMethod.DigitalWallet, 1.10m);
  var report = await reporting.GenerateDailyZReportAsync(DateTime.Today);
  Check(report.TotalInvoicesCount == 4 && report.NetSales == 3.30m && report.CashTotal == 0 && report.CardTotal == 0 && report.DigitalWalletTotal == 1.10m && report.CustomerCreditTotal == 2.20m, "daily report subtracts refunds in every payment category");
  Check(report.TotalGrossSales - report.TotalDiscountsGiven == report.NetSales && report.TotalTaxCollected == 0.10m && report.ExpectedRegisterCash == 100, "report totals reconcile with expected register and net tax");
  Check((await reporting.GenerateDailyZReportAsync(DateTime.Today.AddDays(-1))).TotalInvoicesCount == 0, "date filter excludes other days");
  var bytes = printer.GenerateDailyZReportBytes(report, "Corner Store", "Main Street");
  Check(Encoding.ASCII.GetString(bytes).Contains("OFFICIAL DAILY Z-REPORT") && bytes.TakeLast(4).SequenceEqual(new byte[] { 0x1D, 0x56, 0x41, 0x10 }), "official Z-report ESC/POS content and cut");
  var competingSale = await billing.CheckoutAsync(shiftId, null, [(5, 1, 0)], 0, PaymentMethod.Card, 1.10m);
  async Task<bool> Compete()
  {
   try { return await returns.ProcessItemReturnAsync(competingSale.Id, 5, 1, 1.10m, "Concurrent return"); }
   catch (ArgumentException) { return false; }
  }
  var concurrent = await Task.WhenAll(Task.Run(Compete), Task.Run(Compete));
  Check(concurrent.Count(x => x) == 1, "concurrent returns serialize and cannot double-refund one purchased unit");
  var settings = new TerminalSettings { DataDirectory = directory }; var pos = new PosViewModel(factory, billing, printer, settings); await pos.InitializeAsync();
  var inventoryVm = new InventoryViewModel(inventory); var returnsVm = new ReturnsViewModel(returns); var reportsVm = new ReportsViewModel(reporting, printer, settings, pos);
  var shell = new ShellViewModel(pos, inventoryVm, returnsVm, reportsVm, settings);
  var window = new ShellWindow(shell) { ShowActivated = false, Left = -10000, Top = -10000 };
  var output = Path.Combine(AppContext.BaseDirectory, "verification"); Directory.CreateDirectory(output);
  window.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
  Render(window, Path.Combine(output, "shell-pos.png"));
  await shell.NavigateCommand.ExecuteAsync("Inventory"); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
  Check(shell.CurrentPageViewModel == inventoryVm && inventoryVm.Products.Count == 5, "shell inventory navigation loads catalog");
  inventoryVm.SelectProductCommand.Execute(inventoryVm.Products.First());
  Check(inventoryVm.IsEditing && inventoryVm.FormBarcode.Length > 0, "inventory edit action populates selected product form");
  await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
  Render(window, Path.Combine(output, "shell-inventory.png"));
  inventoryVm.FormName = "Bread 400 g fresh"; await inventoryVm.SaveProductCommand.ExecuteAsync(null);
  Check(!inventoryVm.IsError && !inventoryVm.IsEditing && inventoryVm.Products.Any(p => p.Name == "Bread 400 g fresh"), "inventory save command persists edit and resets form");
  inventoryVm.BeginAdjustmentCommand.Execute(inventoryVm.Products.First(p => p.Id == 3));
  Check(inventoryVm.IsAdjustmentExpanded, "stock adjust action expands its audit panel");
  await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Render(window, Path.Combine(output, "shell-stock-adjustment.png"));
  inventoryVm.AdjustmentQuantity = -1; inventoryVm.AdjustmentReason = "Physical audit";
  await inventoryVm.ApplyAdjustmentCommand.ExecuteAsync(null);
  Check(!inventoryVm.IsError && inventoryVm.FormStockQuantity == 99, "inventory adjustment command refreshes selected stock");
  await shell.NavigateCommand.ExecuteAsync("Returns"); returnsVm.SearchQuery = credit.InvoiceNumber; await returnsVm.SearchInvoiceCommand.ExecuteAsync(null);
  returnsVm.SelectReturnLineCommand.Execute(returnsVm.LineItems[0]);
  Check(returnsVm.RefundAmount == 2.20m && returnsVm.SelectedLine!.AvailableQuantity == 1, "returns page calculates remaining exact refund");
  await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Render(window, Path.Combine(output, "shell-returns.png"));
  returnsVm.Reason = "Final credit return"; await returnsVm.ProcessRefundCommand.ExecuteAsync(null);
  Check(!returnsVm.IsError && returnsVm.LineItems[0].AvailableQuantity == 0 && returnsVm.SelectedLine is null, "refund command refreshes invoice and prevents repeat submission");
  await shell.NavigateCommand.ExecuteAsync("Reports"); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Render(window, Path.Combine(output, "shell-reports.png"));
  await reportsVm.PrintReportCommand.ExecuteAsync(null);
  Check(File.Exists(Path.Combine(directory, "reports", $"Z-{DateTime.Today:yyyyMMdd}.bin")), "report print command saves payload without configured hardware");
  shell.ToggleSidebarCommand.Execute(null); window.Width = 1200; window.Height = 800;
  await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Render(window, Path.Combine(output, "shell-collapsed-minimum.png"));
  Check(!shell.IsSidebarExpanded && window.FindName("PageContent") is System.Windows.Controls.ContentControl, "collapsed navigation and minimum window render");
  window.Hide();
  var closed = await billing.CloseShiftAsync(shiftId, 100);
  Check(closed.CashSalesTotal == 0 && closed.CardSalesTotal == 0, "shift reconciliation subtracts cash and card refunds");
  await using (var db = await factory.CreateDbContextAsync())
  {
   await using var tx = await db.Database.BeginTransactionAsync();
   var boundaryShift = new CashierShift { CashierId = 2, OpeningFloat = 20, OpenedAtUtc = DateTime.Today.AddDays(-1).AddHours(8).ToUniversalTime() };
   db.CashierShifts.Add(boundaryShift);
   db.Invoices.Add(new Invoice { InvoiceNumber = "BOUNDARY-YESTERDAY", Shift = boundaryShift, CreatedAtUtc = DateTime.Today.AddDays(-1).AddHours(23).ToUniversalTime(), SubTotal = 5, GrandTotal = 5, PaymentType = PaymentMethod.Cash, PaidAmount = 5 });
   db.Invoices.Add(new Invoice { InvoiceNumber = "BOUNDARY-TODAY", Shift = boundaryShift, CreatedAtUtc = DateTime.Today.ToUniversalTime(), SubTotal = 7, GrandTotal = 7, PaymentType = PaymentMethod.Cash, PaidAmount = 7 });
   await db.SaveChangesAsync(); await tx.CommitAsync();
  }
  var yesterday = await reporting.GenerateDailyZReportAsync(DateTime.Today.AddDays(-1));
  Check(yesterday.CashTotal == 5 && yesterday.TotalInvoicesCount == 1 && yesterday.ExpectedRegisterCash == 25, "historical Z-report uses local midnight and excludes later shift transactions");
 }
 private static void Render(Window window, string path)
 {
  var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
  target.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
  using var stream = File.Create(path); encoder.Save(stream);
 }
 private sealed class Factory(string path) : IDbContextFactory<AppDbContext>
 {
  public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Foreign Keys=True").Options);
 }
}


