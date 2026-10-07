using Microsoft.EntityFrameworkCore;
using ShopBilling.Core.Entities;
using ShopBilling.Core.Enums;
using ShopBilling.Infrastructure.Services;
namespace ShopBilling.Infrastructure.Data;
public class AppDbContext : DbContext
{
 public AppDbContext() { }
 public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
 public DbSet<User> Users => Set<User>();
 public DbSet<Customer> Customers => Set<Customer>();
 public DbSet<Product> Products => Set<Product>();
 public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
 public DbSet<Invoice> Invoices => Set<Invoice>();
 public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
 public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
 public DbSet<ItemReturn> ItemReturns => Set<ItemReturn>();
 protected override void OnConfiguring(DbContextOptionsBuilder options)
 {
  if (!options.IsConfigured) options.UseSqlite("Data Source=pos_store.db;Foreign Keys=True;Default Timeout=15");
 }
 protected override void OnModelCreating(ModelBuilder model)
 {
  model.Entity<User>().HasIndex(x => x.Username).IsUnique();
  model.Entity<Product>().Ignore(x => x.IsLowStock);
  model.Entity<Product>().Ignore(x => x.ExpectedStockQuantity);
  model.Entity<Product>().HasIndex(x => x.Barcode).IsUnique();
  model.Entity<Customer>().HasIndex(x => x.PhoneNumber).IsUnique();
  model.Entity<Invoice>().HasIndex(x => x.InvoiceNumber).IsUnique();
  model.Entity<InvoiceItem>().Ignore(x => x.Total);
  model.Entity<CashierShift>().HasOne(x => x.Cashier).WithMany().HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
  model.Entity<Invoice>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.CashierShiftId).OnDelete(DeleteBehavior.Restrict);
  model.Entity<Invoice>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
  model.Entity<InvoiceItem>().HasOne(x => x.Invoice).WithMany(x => x.Items).HasForeignKey(x => x.InvoiceId);
  model.Entity<InvoiceItem>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
  model.Entity<StockAdjustment>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
  model.Entity<ItemReturn>().HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
  model.Entity<ItemReturn>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
  model.Entity<ItemReturn>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.CashierShiftId).OnDelete(DeleteBehavior.Restrict);
  model.Entity<ItemReturn>().HasIndex(x => new { x.InvoiceId, x.ProductId });
  model.Entity<ItemReturn>().HasIndex(x => x.CreatedAtUtc);
  foreach (var entity in model.Model.GetEntityTypes())
   foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(decimal)))
   { property.SetColumnType("TEXT"); property.SetPrecision(18); property.SetScale(2); }
  model.Entity<User>().HasData(
   new User { Id = 1, Username = "admin", FullName = "Admin", Role = UserRole.Admin, PasswordHash = PasswordHasher.Hash("1234", "admin-seed-salt!1"u8.ToArray()) },
   new User { Id = 2, Username = "cashier", FullName = "Cashier", Role = UserRole.Cashier, PasswordHash = PasswordHasher.Hash("1234", "cashier-seed-salt"u8.ToArray()) });
  model.Entity<Customer>().HasData(new Customer { Id = 1, Name = "Walk-in Customer", PhoneNumber = "0000000000" });
  model.Entity<Product>().HasData(
   new Product { Id = 1, Barcode = "89010001", Name = "Milk 1 L", CostPrice = 1.20m, SellingPrice = 1.80m, TaxRatePercent = 5, StockQuantity = 100 },
   new Product { Id = 2, Barcode = "89010002", Name = "White Rice 1 kg", CostPrice = 1.50m, SellingPrice = 2.20m, StockQuantity = 100 },
   new Product { Id = 3, Barcode = "89010003", Name = "Bread 400 g", CostPrice = 0.80m, SellingPrice = 1.30m, TaxRatePercent = 5, StockQuantity = 100 },
   new Product { Id = 4, Barcode = "89010004", Name = "Tea 200 g", CostPrice = 2.50m, SellingPrice = 3.50m, TaxRatePercent = 10, StockQuantity = 100 },
   new Product { Id = 5, Barcode = "89010005", Name = "Laundry Soap", CostPrice = 0.60m, SellingPrice = 1.00m, TaxRatePercent = 10, StockQuantity = 100 });
 }
}
