using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Enums;
using ShopBilling.Core.Services;
using ShopBilling.Infrastructure.Data;
using ShopBilling.Infrastructure.Services;
using ShopBilling.Wpf.Services;
using ShopBilling.Wpf.ViewModels;
using ShopBilling.Wpf.ViewModels.Pages;
using ShopBilling.Wpf.Views;
namespace ShopBilling.Wpf;
public partial class App : Application
{
 private ServiceProvider? services;
 protected override async void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  try
  {
   var settings = TerminalSettings.Load(); Directory.CreateDirectory(settings.DataDirectory);
   var collection = new ServiceCollection();
   collection.AddSingleton(settings);
   collection.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(settings.DataDirectory, "pos_store.db")};Foreign Keys=True;Default Timeout=15"));
   collection.AddTransient<BillingService>(); collection.AddTransient<IBillingService>(s => s.GetRequiredService<BillingService>());
   collection.AddSingleton<EscPosPrintService>(); collection.AddSingleton<IReceiptPrinter>(s => s.GetRequiredService<EscPosPrintService>());
   collection.AddSingleton<InventoryService>(); collection.AddSingleton<IInventoryService>(s => s.GetRequiredService<InventoryService>());
   collection.AddSingleton<ReturnService>(); collection.AddSingleton<IReturnService>(s => s.GetRequiredService<ReturnService>());
   collection.AddSingleton<ReportingService>(); collection.AddSingleton<IReportingService>(s => s.GetRequiredService<ReportingService>());
   collection.AddSingleton<PosViewModel>(); collection.AddSingleton<InventoryViewModel>(); collection.AddSingleton<ReportsViewModel>(); collection.AddSingleton<ReturnsViewModel>();
   collection.AddSingleton<ShellViewModel>(); collection.AddTransient<ShellWindow>();
   services = collection.BuildServiceProvider();
   await using (var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
   {
    await DatabaseInitializer.InitializeAsync(db);
    await using var transaction = await db.Database.BeginTransactionAsync();
    var admin = await db.Users.SingleAsync(u => u.Username == "admin" && u.IsActive);
    if (!await db.CashierShifts.AnyAsync(s => s.CashierId == admin.Id && s.Status == ShiftStatus.Open))
    {
     db.CashierShifts.Add(new CashierShift { CashierId = admin.Id, OpeningFloat = 100m });
     await db.SaveChangesAsync();
    }
    await transaction.CommitAsync();
   }
   await services.GetRequiredService<PosViewModel>().InitializeAsync();
   MainWindow = services.GetRequiredService<ShellWindow>(); MainWindow.Show();
  }
  catch (Exception ex) { MessageBox.Show(ex.ToString(), "Shop Billing startup failed", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
 }
 protected override void OnExit(ExitEventArgs e) { services?.Dispose(); base.OnExit(e); }
}
