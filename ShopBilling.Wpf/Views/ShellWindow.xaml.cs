using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShopBilling.Wpf.ViewModels;
using ShopBilling.Wpf.Views.Pages;
namespace ShopBilling.Wpf.Views;
public partial class ShellWindow : Window
{
 private readonly ShellViewModel shell;
 private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
 public ShellWindow(ShellViewModel shell)
 {
  InitializeComponent(); DataContext = this.shell = shell;
  timer.Tick += (_, _) => shell.Pos.CurrentTime = DateTime.Now;
  Loaded += (_, _) => timer.Start(); Closed += (_, _) => timer.Stop();
  PreviewKeyDown += (_, e) =>
  {
   if (shell.CurrentPageViewModel != shell.Pos) return;
   var pos = FindPosView(PageContent);
   if (e.Key == Key.F1) { pos?.FocusScanner(); e.Handled = true; return; }
   ICommand? command = e.Key switch { Key.F5 => shell.Pos.PayCashCommand, Key.F6 => shell.Pos.PayCardCommand, Key.F9 => shell.Pos.OpenDrawerCommand, Key.F12 => shell.Pos.ClearCartCommand, _ => null };
   if (command is null) return;
   pos?.CommitEdits(); if (command.CanExecute(null)) command.Execute(null); e.Handled = true;
  };
  Closing += (_, e) =>
  {
   if (shell.IsBusy) { e.Cancel = true; return; }
   if (shell.Pos.CartItems.Count > 0 && MessageBox.Show("Discard the current cart and exit?", "Unsaved cart", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
  };
 }
 private static PosView? FindPosView(DependencyObject parent)
 {
  for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
  {
   var child = VisualTreeHelper.GetChild(parent, i);
   if (child is PosView view) return view;
   var result = FindPosView(child); if (result is not null) return result;
  }
  return null;
 }
}
