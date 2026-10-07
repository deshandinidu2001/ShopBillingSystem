using ShopBilling.Core.Enums;
namespace ShopBilling.Core.Entities;
public class User
{
 public int Id { get; set; }
 public string Username { get; set; } = string.Empty;
 public string PasswordHash { get; set; } = string.Empty;
 public string FullName { get; set; } = string.Empty;
 public UserRole Role { get; set; } = UserRole.Cashier;
 public bool IsActive { get; set; } = true;
}
public class Customer
{
 public int Id { get; set; }
 public string Name { get; set; } = string.Empty;
 public string PhoneNumber { get; set; } = string.Empty;
 public decimal OutstandingBalance { get; set; }
 public decimal CreditLimit { get; set; }
}
public class Product
{
 public int Id { get; set; }
 public string Barcode { get; set; } = string.Empty;
 public string Name { get; set; } = string.Empty;
 public decimal CostPrice { get; set; }
 public decimal SellingPrice { get; set; }
 public decimal TaxRatePercent { get; set; }
 public int StockQuantity { get; set; }
 public int LowStockThreshold { get; set; } = 10;
 public int? ExpectedStockQuantity { get; set; }
 public bool IsLowStock => StockQuantity <= LowStockThreshold;
 public bool IsActive { get; set; } = true;
}
public class CashierShift
{
 public int Id { get; set; }
 public int CashierId { get; set; }
 public User Cashier { get; set; } = null!;
 public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
 public DateTime? ClosedAtUtc { get; set; }
 public decimal OpeningFloat { get; set; }
 public decimal CashSalesTotal { get; set; }
 public decimal CardSalesTotal { get; set; }
 public decimal ActualClosingCash { get; set; }
 public ShiftStatus Status { get; set; } = ShiftStatus.Open;
}
public class Invoice
{
 public int Id { get; set; }
 public string InvoiceNumber { get; set; } = string.Empty;
 public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
 public int? CashierShiftId { get; set; }
 public CashierShift? Shift { get; set; }
 public int? CustomerId { get; set; }
 public Customer? Customer { get; set; }
 public PaymentMethod PaymentType { get; set; }
 public decimal SubTotal { get; set; }
 public decimal TaxTotal { get; set; }
 public decimal DiscountTotal { get; set; }
 public decimal GrandTotal { get; set; }
 public decimal PaidAmount { get; set; }
 public decimal ChangeAmount { get; set; }
 public List<InvoiceItem> Items { get; set; } = [];
}
public class InvoiceItem
{
 public int Id { get; set; }
 public int InvoiceId { get; set; }
 public Invoice Invoice { get; set; } = null!;
 public int ProductId { get; set; }
 public Product Product { get; set; } = null!;
 public int Quantity { get; set; }
 public decimal UnitPrice { get; set; }
 public decimal LineDiscount { get; set; }
 public decimal TaxRatePercent { get; set; }
 public decimal Total => Quantity * UnitPrice - LineDiscount;
}
