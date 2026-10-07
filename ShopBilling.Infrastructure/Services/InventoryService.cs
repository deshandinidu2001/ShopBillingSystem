using Microsoft.EntityFrameworkCore;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Services;
using ShopBilling.Infrastructure.Data;
namespace ShopBilling.Infrastructure.Services;
public class InventoryService(IDbContextFactory<AppDbContext> factory) : IInventoryService
{
 public async Task<List<Product>> GetAllProductsAsync(string? searchQuery = null, bool includeInactive = false)
 {
  await using var db = await factory.CreateDbContextAsync();
  var query = db.Products.AsNoTracking();
  if (!includeInactive) query = query.Where(p => p.IsActive);
  if (!string.IsNullOrWhiteSpace(searchQuery))
  {
   var term = searchQuery.Trim().ToLower();
   query = query.Where(p => p.Name.ToLower().Contains(term) || p.Barcode.ToLower().Contains(term));
  }
  return await query.OrderBy(p => p.Name).ToListAsync();
 }
 public async Task<Product> CreateOrUpdateProductAsync(Product product)
 {
  ArgumentNullException.ThrowIfNull(product);
  var barcode = product.Barcode.Trim(); var name = product.Name.Trim();
  if (barcode.Length is < 1 or > 64 || name.Length is < 1 or > 150) throw new ArgumentException("Provide a barcode (up to 64 characters) and product name (up to 150 characters).");
  if (product.CostPrice < 0 || product.SellingPrice < 0 || Money.Round(product.CostPrice) != product.CostPrice || Money.Round(product.SellingPrice) != product.SellingPrice) throw new ArgumentException("Prices must be nonnegative amounts with at most two decimal places.");
  if (product.TaxRatePercent < 0 || product.TaxRatePercent > 100 || product.StockQuantity < 0 || product.LowStockThreshold < 0) throw new ArgumentException("Tax must be 0–100%; stock and threshold must be nonnegative.");
  await using var db = await factory.CreateDbContextAsync();
  await using var transaction = await db.Database.BeginTransactionAsync();
  if (await db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != product.Id)) throw new InvalidOperationException("This barcode already belongs to another product.");
  Product saved;
  if (product.Id == 0) { saved = new Product(); db.Products.Add(saved); }
  else saved = await db.Products.FindAsync(product.Id) ?? throw new InvalidOperationException("Product no longer exists.");
  if (product.ExpectedStockQuantity.HasValue && saved.StockQuantity != product.ExpectedStockQuantity.Value) throw new InvalidOperationException("Stock changed since this product was selected. Reload and review its current quantity before saving.");
  var oldStock = saved.StockQuantity;
  saved.Barcode = barcode; saved.Name = name; saved.CostPrice = product.CostPrice; saved.SellingPrice = product.SellingPrice;
  saved.TaxRatePercent = product.TaxRatePercent; saved.StockQuantity = product.StockQuantity; saved.LowStockThreshold = product.LowStockThreshold; saved.IsActive = product.IsActive;
  if (oldStock != saved.StockQuantity) db.StockAdjustments.Add(new StockAdjustment { Product = saved, QuantityChange = saved.StockQuantity - oldStock, StockAfter = saved.StockQuantity, Reason = product.Id == 0 ? "Opening stock on product creation" : "Stock correction in product master" });
  await db.SaveChangesAsync(); await transaction.CommitAsync(); return saved;
 }
 public async Task<bool> AdjustStockAsync(int productId, int quantityChange, string reason)
 {
  if (quantityChange == 0 || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A nonzero quantity change and reason are required.");
  if (reason.Trim().Length > 500) throw new ArgumentException("Reason must be at most 500 characters.");
  await using var db = await factory.CreateDbContextAsync();
  await using var transaction = await db.Database.BeginTransactionAsync();
  var product = await db.Products.FindAsync(productId) ?? throw new InvalidOperationException("Product was not found.");
  var stock = checked(product.StockQuantity + quantityChange);
  if (stock < 0) throw new InvalidOperationException("This adjustment would make stock negative.");
  product.StockQuantity = stock;
  db.StockAdjustments.Add(new StockAdjustment { ProductId = productId, QuantityChange = quantityChange, StockAfter = stock, Reason = reason.Trim() });
  await db.SaveChangesAsync(); await transaction.CommitAsync(); return true;
 }
 public async Task<List<Product>> GetLowStockAlertsAsync()
 {
  await using var db = await factory.CreateDbContextAsync();
  return await db.Products.AsNoTracking().Where(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold).OrderBy(p => p.StockQuantity).ToListAsync();
 }
}
