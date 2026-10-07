using CommunityToolkit.Mvvm.ComponentModel;
using ShopBilling.Core.Services;
namespace ShopBilling.Wpf.ViewModels;
public partial class CartItemViewModel : ObservableObject
{
 public int ProductId { get; init; }
 public string Barcode { get; init; } = string.Empty;
 public string ProductName { get; init; } = string.Empty;
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubTotal), nameof(TaxAmount), nameof(Total))] private decimal unitPrice;
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubTotal), nameof(TaxAmount), nameof(Total))] private int quantity = 1;
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubTotal), nameof(TaxAmount), nameof(Total))] private decimal lineDiscount;
 [ObservableProperty] [NotifyPropertyChangedFor(nameof(TaxAmount), nameof(Total))] private decimal taxRatePercent;
 public decimal SubTotal => UnitPrice * Quantity - LineDiscount;
 public decimal TaxAmount => Money.Round(SubTotal * TaxRatePercent / 100m);
 public decimal Total => SubTotal + TaxAmount;
 public void UpdateQuantity(int delta) => Quantity = checked(Quantity + delta);
}
