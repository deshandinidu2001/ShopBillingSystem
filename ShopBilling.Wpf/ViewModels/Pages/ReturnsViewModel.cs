using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Models;
using ShopBilling.Core.Services;
namespace ShopBilling.Wpf.ViewModels.Pages;
public partial class ReturnsViewModel(IReturnService returns) : PageViewModel
{
 [ObservableProperty] private string searchQuery = string.Empty;
 public ObservableCollection<Invoice> MatchingInvoices { get; } = [];
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(CustomerSummary), nameof(PurchaseTime))] private Invoice? currentInvoice;
 public ObservableCollection<ReturnLineModel> LineItems { get; } = [];
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(RefundAmount), nameof(RefundTax), nameof(RefundDiscount), nameof(HasSelectedLine))] private ReturnLineModel? selectedLine;
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(RefundAmount), nameof(RefundTax), nameof(RefundDiscount))] private int returnQuantity = 1;
 [ObservableProperty] private string reason = string.Empty;
 public string CustomerSummary => CurrentInvoice?.Customer is { } customer ? $"{customer.Name} • {customer.PhoneNumber}" : "Walk-in customer";
 public string PurchaseTime => CurrentInvoice?.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy • HH:mm") ?? "";
 public bool HasSelectedLine => SelectedLine is not null;
 private bool ValidQuantity => SelectedLine is not null && ReturnQuantity > 0 && ReturnQuantity <= SelectedLine.AvailableQuantity;
 public decimal RefundAmount => ValidQuantity ? SelectedLine!.RefundFor(ReturnQuantity) : 0;
 public decimal RefundTax => ValidQuantity ? SelectedLine!.TaxFor(ReturnQuantity) : 0;
 public decimal RefundDiscount => ValidQuantity ? SelectedLine!.GrossFor(ReturnQuantity) + RefundTax - RefundAmount : 0;
 [RelayCommand] private Task SearchInvoiceAsync() => ExecuteAsync(async () =>
 {
  if (string.IsNullOrWhiteSpace(SearchQuery)) throw new ArgumentException("Enter an exact invoice number or customer phone number.");
  CurrentInvoice = null; LineItems.Clear(); SelectedLine = null; MatchingInvoices.Clear();
  var matches = await returns.SearchInvoicesAsync(SearchQuery);
  foreach (var match in matches) MatchingInvoices.Add(match);
  if (matches.Count == 0) throw new InvalidOperationException("No invoices found for this number or phone.");
  if (matches.Count == 1) await LoadInvoiceAsync(matches[0].InvoiceNumber);
  StatusMessage = $"{matches.Count} invoice(s) found. Choose a purchase, then an item to return.";
 });
 [RelayCommand] private Task SelectInvoiceAsync(Invoice invoice) => ExecuteAsync(() => LoadInvoiceAsync(invoice.InvoiceNumber));
 private async Task LoadInvoiceAsync(string invoiceNumber)
 {
  CurrentInvoice = await returns.FindInvoiceForReturnAsync(invoiceNumber) ?? throw new InvalidOperationException("Invoice was not found.");
  LineItems.Clear(); foreach (var line in await returns.GetReturnLinesAsync(CurrentInvoice.Id)) LineItems.Add(line);
  SelectedLine = null; ReturnQuantity = 1;
 }
 [RelayCommand] private void SelectReturnLine(ReturnLineModel line)
 {
  if (IsBusy) return;
  if (line.AvailableQuantity <= 0) { IsError = true; StatusMessage = "This item has already been returned in full."; return; }
  SelectedLine = line; ReturnQuantity = 1; IsError = false; StatusMessage = "Review the refund quantity and enter a reason.";
 }
 [RelayCommand] private Task ProcessRefundAsync() => ExecuteAsync(async () =>
 {
  if (CurrentInvoice is null || !ValidQuantity) throw new InvalidOperationException("Choose an item and a quantity within the remaining purchase quantity.");
  var amount = RefundAmount; var number = CurrentInvoice.InvoiceNumber;
  await returns.ProcessItemReturnAsync(CurrentInvoice.Id, SelectedLine!.ProductId, ReturnQuantity, amount, Reason);
  await LoadInvoiceAsync(number); Reason = string.Empty;
  StatusMessage = $"Refund {amount:C2} recorded against {number}; stock restored. {CurrentInvoice.PaymentType} payout/reversal must be completed through the original payment channel.";
 });
}
