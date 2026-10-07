using System.Security.Cryptography;
namespace ShopBilling.Infrastructure.Services;
public static class PasswordHasher
{
 public static string Hash(string password, byte[]? salt = null)
 {
  salt ??= RandomNumberGenerator.GetBytes(16);
  var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
  return $"PBKDF2-SHA256$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
 }
 public static bool Verify(string password, string encoded)
 {
  try
  {
   var parts = encoded.Split('$');
   if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" || !int.TryParse(parts[1], out var iterations) || iterations < 10000 || iterations > 1000000) return false;
   var expected = Convert.FromBase64String(parts[3]);
   var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
   return CryptographicOperations.FixedTimeEquals(actual, expected);
  }
  catch (FormatException) { return false; }
 }
}
