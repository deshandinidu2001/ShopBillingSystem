using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopBilling.Core.Models;
using ShopBilling.Core.Services;
using ShopBilling.Wpf.Services;
namespace ShopBilling.Wpf.ViewModels.Pages;
public partial class ReportsViewModel(IReportingService reporting, IReceiptPrinter printer, TerminalSettings settings, PosViewModel pos) : PageViewModel
{
 [ObservableProperty] private DateTime selectedDate = DateTime.Today;
 [ObservableProperty] private DailyZReportModel report = new() { Date = DateTime.Today };
 public PosViewModel Pos => pos;
 partial void OnSelectedDateChanged(DateTime value) { if (!IsBusy) _ = LoadReportAsync(); }
 [RelayCommand] private Task LoadReportAsync() => ExecuteAsync(async () =>
 {
  Report = await reporting.GenerateDailyZReportAsync(SelectedDate);
  StatusMessage = $"Daily register summary for {SelectedDate:dd MMM yyyy}. Refunds are included on their processing date.";
 });
 [RelayCommand] private Task PrintReportAsync() => ExecuteAsync(async () =>
 {
  Report = await reporting.GenerateDailyZReportAsync(SelectedDate);
  var bytes = printer.GenerateDailyZReportBytes(Report, settings.StoreName, settings.StoreAddress);
  var directory = Path.Combine(settings.DataDirectory, "reports"); Directory.CreateDirectory(directory);
  await File.WriteAllBytesAsync(Path.Combine(directory, $"Z-{SelectedDate:yyyyMMdd}.bin"), bytes);
  if (string.IsNullOrWhiteSpace(settings.PrinterAddress)) StatusMessage = "Z-report saved locally. Configure a network printer to print it.";
  else { await printer.PrintToNetworkPrinterAsync(settings.PrinterAddress, settings.PrinterPort, bytes); StatusMessage = "Official daily Z-report sent to the printer and saved locally."; }
 });
 [RelayCommand] private Task CloseShiftAsync() => ExecuteAsync(async () =>
 {
  await pos.CloseShiftCommand.ExecuteAsync(null);
  if (pos.IsError) throw new InvalidOperationException(pos.StatusMessage);
  Report = await reporting.GenerateDailyZReportAsync(SelectedDate); StatusMessage = pos.StatusMessage;
 });
}
