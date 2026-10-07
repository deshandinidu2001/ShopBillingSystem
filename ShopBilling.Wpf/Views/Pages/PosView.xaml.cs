using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ShopBilling.Wpf.ViewModels;
namespace ShopBilling.Wpf.Views.Pages;
public partial class PosView : UserControl
{
 private PosViewModel viewModel => (PosViewModel)DataContext;
 private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
 private readonly System.Text.StringBuilder scanBuffer = new();
 private DateTime lastScanKey;
 public PosView()
 {
  InitializeComponent();
  PreviewKeyDown += HandleKeyDown; PreviewTextInput += HandleScannerText;
  Loaded += (_, _) => { BarcodeBox.Focus(); clock.Start(); };
  clock.Tick += (_, _) => viewModel.CurrentTime = DateTime.Now;

  Unloaded += (_, _) => clock.Stop();
 }
 public void FocusScanner() { BarcodeBox.Focus(); BarcodeBox.SelectAll(); }
 private void HandleKeyDown(object sender, KeyEventArgs e)
 {
  ICommand? command = e.Key switch { Key.F5 => viewModel.PayCashCommand, Key.F6 => viewModel.PayCardCommand, Key.F9 => viewModel.OpenDrawerCommand, Key.F12 => viewModel.ClearCartCommand, _ => null };
  if (e.Key == Key.F1) { BarcodeBox.Focus(); BarcodeBox.SelectAll(); e.Handled = true; }
  else if (command is not null) { CommitEdits(); if (command.CanExecute(null)) command.Execute(null); e.Handled = true; }
  else if (e.Key == Key.Enter && scanBuffer.Length >= 4 && DateTime.UtcNow - lastScanKey < TimeSpan.FromMilliseconds(120) && Keyboard.FocusedElement is not TextBox)
  {
   viewModel.BarcodeInput = scanBuffer.ToString(); scanBuffer.Clear();
   if (viewModel.AddBarcodeCommand.CanExecute(null)) viewModel.AddBarcodeCommand.Execute(null);
   e.Handled = true;
  }
 }
 private void HandleScannerText(object sender, TextCompositionEventArgs e)
 {
  // Focused text fields retain normal typing. F1 directs a keyboard-wedge scanner to the barcode field.
  if (Keyboard.FocusedElement is TextBox) { scanBuffer.Clear(); return; }
  if (DateTime.UtcNow - lastScanKey > TimeSpan.FromMilliseconds(120)) scanBuffer.Clear();
  lastScanKey = DateTime.UtcNow;
  if (e.Text.All(char.IsLetterOrDigit)) scanBuffer.Append(e.Text); else scanBuffer.Clear();
 }
 public void CommitEdits() { CartGrid.CommitEdit(DataGridEditingUnit.Cell, true); CartGrid.CommitEdit(DataGridEditingUnit.Row, true); Keyboard.ClearFocus(); }
}
