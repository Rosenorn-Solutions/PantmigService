using System.Security.Cryptography;
using System.Text;

namespace PantmigShared;

public static class CityKey
{
 // Fixed namespace GUID (randomly generated once). Keep constant for determinism across services.
 private static readonly Guid Namespace = Guid.Parse("7a9cf65b-8a53-4d98-9c5d-3a2c9f6a8b5f");

 public static Guid FromSlug(string slug)
 {
 slug = slug?.Trim().ToLowerInvariant() ?? string.Empty;
 var nsBytes = Namespace.ToByteArray();
 var nameBytes = Encoding.UTF8.GetBytes(slug);

 // RFC4122 version5 uses SHA-1 over namespace bytes + name bytes
 Span<byte> data = stackalloc byte[nsBytes.Length + nameBytes.Length];
 nsBytes.CopyTo(data);
 nameBytes.CopyTo(data[nsBytes.Length..]);
 var hash = SHA1.HashData(data); //20 bytes

 // Use first16 bytes to form GUID, then set version and variant bits
 Span<byte> guidBytes = stackalloc byte[16];
 hash.AsSpan(0,16).CopyTo(guidBytes);

 // Set version (5 =>0101)
 guidBytes[6] = (byte)((guidBytes[6] &0x0F) |0x50);
 // Set variant (RFC4122 =>10xx xxxx)
 guidBytes[8] = (byte)((guidBytes[8] &0x3F) |0x80);

 return new Guid(guidBytes);
 }
}
