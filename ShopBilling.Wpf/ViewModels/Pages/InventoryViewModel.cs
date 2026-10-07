using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Services;
namespace ShopBilling.Wpf.ViewModels.Pages;
public partial class InventoryViewModel(IInventoryService inventory) : PageViewModel
{
 public ObservableCollection<Product> Products { get; } = [];
 [ObservableProperty] private Product? selectedProduct;
 [ObservableProperty] private string searchQuery = string.Empty;
 [ObservableProperty] private bool includeInactive;
 [ObservableProperty] private string formBarcode = string.Empty;
 [ObservableProperty] private string formName = string.Empty;
 [ObservableProperty] private decimal formCostPrice;
 [ObservableProperty] private decimal formSellingPrice;
 [ObservableProperty] private decimal formTaxRate;
 [ObservableProperty] private int formStockQuantity;
 [ObservableProperty] private int formLowStockThreshold = 10;
 [ObservableProperty] private bool formIsActive = true;
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(FormTitle))] private bool isEditing;
 [ObservableProperty] private int adjustmentQuantity = 1;
 [ObservableProperty] private string adjustmentReason = string.Empty;
 [ObservableProperty] private int lowStockCount;
 [ObservableProperty] private bool isAdjustmentExpanded;
 private CancellationTokenSource? searchDelay;
 public string FormTitle => IsEditing ? "Product details" : "Add a new product";
 partial void OnSelectedProductChanged(Product? value) { if (value is not null) SelectProduct(value); }
 partial void OnSearchQueryChanged(string value) => QueueSearch();
 partial void OnIncludeInactiveChanged(bool value) => QueueSearch();
 private async void QueueSearch()
 {
  searchDelay?.Cancel(); var delay = searchDelay = new CancellationTokenSource();
  try { await Task.Delay(250, delay.Token); while (IsBusy) await Task.Delay(50, delay.Token); await LoadProductsAsync(); }
  catch (OperationCanceledException) { }
  finally { delay.Dispose(); if (ReferenceEquals(delay, searchDelay)) searchDelay = null; }
 }
 [RelayCommand] private Task LoadProductsAsync() => ExecuteAsync(LoadCoreAsync);
 private async Task LoadCoreAsync()
 {
  var items = await inventory.GetAllProductsAsync(SearchQuery, IncludeInactive);
  Products.Clear(); foreach (var item in items) Products.Add(item);
  LowStockCount = (await inventory.GetLowStockAlertsAsync()).Count;
  StatusMessage = $"{Products.Count} products • {LowStockCount} low-stock alerts";
 }
 [RelayCommand] private void SelectProduct(Product p)
 {
  if (IsBusy) return;
  if (!ReferenceEquals(SelectedProduct, p)) { SelectedProduct = p; return; }
  IsEditing = true; FormBarcode = p.Barcode; FormName = p.Name; FormCostPrice = p.CostPrice; FormSellingPrice = p.SellingPrice;
  FormTaxRate = p.TaxRatePercent; FormStockQuantity = p.StockQuantity; FormLowStockThreshold = p.LowStockThreshold; FormIsActive = p.IsActive;
 }
 [RelayCommand] private Task SaveProductAsync() => ExecuteAsync(async () =>
 {
  var product = new Product { Id = IsEditing ? SelectedProduct?.Id ?? 0 : 0, Barcode = FormBarcode, Name = FormName, CostPrice = FormCostPrice, SellingPrice = FormSellingPrice, TaxRatePercent = FormTaxRate, StockQuantity = FormStockQuantity, LowStockThreshold = FormLowStockThreshold, IsActive = FormIsActive, ExpectedStockQuantity = IsEditing ? SelectedProduct?.StockQuantity : null };
  await inventory.CreateOrUpdateProductAsync(product); ResetForm(); await LoadCoreAsync();
 }, "Product saved. Catalog refreshed.");
 [RelayCommand] private void NewProduct() { if (!IsBusy) ResetForm(); }
 private void ResetForm()
 {
  SelectedProduct = null; IsEditing = false; FormBarcode = string.Empty; FormName = string.Empty; FormCostPrice = 0; FormSellingPrice = 0;
  FormTaxRate = 0; FormStockQuantity = 0; FormLowStockThreshold = 10; FormIsActive = true; AdjustmentQuantity = 1; AdjustmentReason = string.Empty; IsAdjustmentExpanded = false;
 }
 [RelayCommand] private Task AdjustStockAsync((int ProductId, int QtyDelta) adjustment) => ExecuteAsync(async () =>
 {
  await inventory.AdjustStockAsync(adjustment.ProductId, adjustment.QtyDelta, AdjustmentReason);
  var selectedId = SelectedProduct?.Id; await LoadCoreAsync();
  var refreshed = Products.FirstOrDefault(p => p.Id == selectedId);
  if (refreshed is not null) { SelectedProduct = refreshed; IsEditing = true; FormStockQuantity = refreshed.StockQuantity; }
 }, "Stock adjustment saved with its audit reason.");
 [RelayCommand] private void BeginAdjustment(Product product)
 {
  SelectProduct(product); IsAdjustmentExpanded = true;
 }
 [RelayCommand] private Task ApplyAdjustmentAsync()
 {
  if (SelectedProduct is null) { IsError = true; StatusMessage = "Select a product before adjusting stock."; return Task.CompletedTask; }
  return AdjustStockAsync((SelectedProduct.Id, AdjustmentQuantity));
 }
}
