using System.IO;
using System.Text.Json;
namespace ShopBilling.Wpf.Services;
public sealed class TerminalSettings
{
 public string StoreName { get; set; } = "Corner Store";
 public string StoreAddress { get; set; } = "Main Street";
 public string PrinterAddress { get; set; } = string.Empty;
 public int PrinterPort { get; set; } = 9100;
 public string DataDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShopBillingSystem");
 public static TerminalSettings Load()
 {
  var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
  return File.Exists(path) ? JsonSerializer.Deserialize<TerminalSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Settings file is empty.") : new TerminalSettings();
 }
}
