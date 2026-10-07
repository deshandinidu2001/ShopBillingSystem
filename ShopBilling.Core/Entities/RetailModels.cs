using ShopBilling.Core.Enums;
namespace ShopBilling.Core.Entities;
public class StockAdjustment
{
 public int Id { get; set; }
 public int ProductId { get; set; }
 public Product Product { get; set; } = null!;
 public int QuantityChange { get; set; }
 public int StockAfter { get; set; }
 public string Reason { get; set; } = string.Empty;
 public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public class ItemReturn
{
 public int Id { get; set; }
 public int InvoiceId { get; set; }
 public Invoice Invoice { get; set; } = null!;
 public int ProductId { get; set; }
 public Product Product { get; set; } = null!;
 public int? CashierShiftId { get; set; }
 public CashierShift? Shift { get; set; }
 public int Quantity { get; set; }
 public decimal RefundAmount { get; set; }
 public decimal GrossAmount { get; set; }
 public decimal TaxAmount { get; set; }
 public decimal DiscountAmount { get; set; }
 public PaymentMethod PaymentType { get; set; }
 public string Reason { get; set; } = string.Empty;
 public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
