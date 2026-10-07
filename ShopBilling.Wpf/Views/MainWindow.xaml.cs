using System.Windows;
using ShopBilling.Wpf.ViewModels;
namespace ShopBilling.Wpf.Views;
public partial class MainWindow : Window
{
 public MainWindow(PosViewModel viewModel)
 {
  InitializeComponent(); DataContext = viewModel;
  Closing += (_, e) =>
  {
   if (viewModel.IsProcessing) { e.Cancel = true; return; }
   if (viewModel.CartItems.Count > 0 && MessageBox.Show("Discard the current cart and exit?", "Unsaved cart", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
  };
 }
}
