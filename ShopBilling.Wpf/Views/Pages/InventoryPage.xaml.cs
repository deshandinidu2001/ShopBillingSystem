using System.Windows.Controls;
namespace ShopBilling.Wpf.Views.Pages;
public partial class InventoryPage : UserControl
{
 public InventoryPage() { InitializeComponent(); }
 private void OnAdjustmentExpanded(object sender, System.Windows.RoutedEventArgs e)
 { Dispatcher.BeginInvoke(() => ((System.Windows.FrameworkElement)sender).BringIntoView()); }
}
