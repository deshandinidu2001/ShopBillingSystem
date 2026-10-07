using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopBilling.Wpf.Services;
using ShopBilling.Wpf.ViewModels.Pages;
namespace ShopBilling.Wpf.ViewModels;
public partial class ShellViewModel : ObservableObject
{
 public PosViewModel Pos { get; }
 public InventoryViewModel Inventory { get; }
 public ReturnsViewModel Returns { get; }
 public ReportsViewModel Reports { get; }
 public string StoreName { get; }
 [ObservableProperty] private object currentPageViewModel;
 [ObservableProperty] private string pageTitle = "POS Checkout";
 [ObservableProperty] private string pageSubtitle = "Fast checkout. Every item accounted for.";
 [ObservableProperty] private string activePage = "POS";
 [ObservableProperty] private bool isSidebarExpanded = true;
 private bool CanNavigate() => !IsBusy;
 public bool IsBusy => Pos.IsProcessing || Inventory.IsBusy || Returns.IsBusy || Reports.IsBusy;
 public ShellViewModel(PosViewModel pos, InventoryViewModel inventory, ReturnsViewModel returns, ReportsViewModel reports, TerminalSettings settings)
 {
  Pos = pos; Inventory = inventory; Returns = returns; Reports = reports; StoreName = settings.StoreName; currentPageViewModel = pos;
  foreach (var page in new ObservableObject[] { pos, inventory, returns, reports }) page.PropertyChanged += (_, args) =>
  {
   if (args.PropertyName is "IsBusy" or "IsProcessing") { OnPropertyChanged(nameof(IsBusy)); NavigateCommand.NotifyCanExecuteChanged(); LogoutCommand.NotifyCanExecuteChanged(); }
  };
 }
 [RelayCommand] private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;
 [RelayCommand(CanExecute = nameof(CanNavigate))] private async Task NavigateAsync(string page)
 {
  if (IsBusy) return;
  try
  {
  ActivePage = page;
  switch (page)
  {
   case "Inventory": CurrentPageViewModel = Inventory; PageTitle = "Item & Inventory Catalog"; PageSubtitle = "A clear view of your products, prices and stock."; await Inventory.LoadProductsCommand.ExecuteAsync(null); break;
   case "Returns": CurrentPageViewModel = Returns; PageTitle = "Returns & Refunds"; PageSubtitle = "Find the purchase. Review the refund. Restore stock."; break;
   case "Reports": await Pos.RefreshShiftAsync(); CurrentPageViewModel = Reports; PageTitle = "Reports & Shift Closing"; PageSubtitle = "Daily performance and register reconciliation."; await Reports.LoadReportCommand.ExecuteAsync(null); break;
   default: await Pos.RefreshShiftAsync(); CurrentPageViewModel = Pos; PageTitle = "POS Checkout"; PageSubtitle = "Fast checkout. Every item accounted for."; ActivePage = "POS"; break;
  }
  }
  catch (Exception ex) { PageSubtitle = $"Unable to refresh this page: {ex.Message}"; }
 }
 [RelayCommand(CanExecute = nameof(CanNavigate))] private Task LogoutAsync() => NavigateAsync("Reports");
}
