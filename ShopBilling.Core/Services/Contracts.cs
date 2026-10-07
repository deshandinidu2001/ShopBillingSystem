using ShopBilling.Core.Entities;
using ShopBilling.Core.Enums;
namespace ShopBilling.Core.Services;
public interface IBillingService
{
 Task<Invoice> CheckoutAsync(int shiftId, int? customerId, List<(int ProductId, int Qty, decimal LineDiscount)> cart, decimal billDiscount, PaymentMethod paymentMethod, decimal amountTendered);
 Task<CashierShift> CloseShiftAsync(int shiftId, decimal actualCash);
}
public interface IReceiptPrinter
{
 byte[] GenerateReceiptBytes(Invoice invoice, string storeName, string storeAddress);
 byte[] GenerateDailyZReportBytes(ShopBilling.Core.Models.DailyZReportModel report, string storeName, string storeAddress);
 byte[] GetCashDrawerKickBytes();
 Task PrintToNetworkPrinterAsync(string ipAddress, int port, byte[] payload);
}
public static class Money
{
 public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public interface IInventoryService
{
 Task<List<Product>> GetAllProductsAsync(string? searchQuery = null, bool includeInactive = false);
 Task<Product> CreateOrUpdateProductAsync(Product product);
 Task<bool> AdjustStockAsync(int productId, int quantityChange, string reason);
 Task<List<Product>> GetLowStockAlertsAsync();
}
public interface IReturnService
{
 Task<Invoice?> FindInvoiceForReturnAsync(string invoiceNumber);
 Task<List<Invoice>> SearchInvoicesAsync(string query);
 Task<List<ShopBilling.Core.Models.ReturnLineModel>> GetReturnLinesAsync(int invoiceId);
 Task<bool> ProcessItemReturnAsync(int invoiceId, int productId, int returnQty, decimal refundAmount, string reason);
}
public interface IReportingService
{
 Task<ShopBilling.Core.Models.DailyZReportModel> GenerateDailyZReportAsync(DateTime date);
}
