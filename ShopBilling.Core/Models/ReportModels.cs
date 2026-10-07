using ShopBilling.Core.Entities;
namespace ShopBilling.Core.Models;
public class DailyZReportModel
{
 public DateTime Date { get; init; }
 public decimal TotalGrossSales { get; init; }
 public decimal TotalTaxCollected { get; init; }
 public decimal TotalDiscountsGiven { get; init; }
 public decimal NetSales { get; init; }
 public int TotalInvoicesCount { get; init; }
 public decimal CashTotal { get; init; }
 public decimal CardTotal { get; init; }
 public decimal DigitalWalletTotal { get; init; }
 public decimal CustomerCreditTotal { get; init; }
 public decimal TotalRefunds { get; init; }
 public int TotalReturnsCount { get; init; }
 public decimal OpeningFloats { get; init; }
 public decimal ExpectedRegisterCash { get; init; }
 public List<CashierShift> Shifts { get; init; } = [];
 public decimal ElectronicTotal => CardTotal + DigitalWalletTotal;
}
public class ReturnLineModel
{
 public int ProductId { get; init; }
 public string ProductName { get; init; } = string.Empty;
 public int PurchasedQuantity { get; init; }
 public int ReturnedQuantity { get; init; }
 public int AvailableQuantity => PurchasedQuantity - ReturnedQuantity;
 public decimal GrossTotal { get; init; }
 public decimal TaxTotal { get; init; }
 public decimal RefundableTotal { get; init; }
 public decimal RefundFor(int quantity) => Portion(RefundableTotal, quantity);
 public decimal TaxFor(int quantity) => Portion(TaxTotal, quantity);
 public decimal GrossFor(int quantity) => Portion(GrossTotal, quantity);
 private decimal Portion(decimal total, int quantity)
 {
  if (quantity <= 0 || quantity > AvailableQuantity) throw new ArgumentException("Return quantity must be between one and the remaining purchased quantity.");
  return Services.Money.Round(total * (ReturnedQuantity + quantity) / PurchasedQuantity) - Services.Money.Round(total * ReturnedQuantity / PurchasedQuantity);
 }
}
