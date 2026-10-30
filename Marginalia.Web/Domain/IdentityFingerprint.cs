using System.Security.Cryptography;
using System.Text;

namespace Marginalia.Web.Domain;

/// <summary>
/// Creates stable privacy-minimizing fingerprints for URL identity suppression.
/// </summary>
public static class IdentityFingerprint
{
    /// <summary>
    /// Hashes a canonical URL identity with SHA-256.
    /// </summary>
    /// <param name="identityKey">A canonical URL identity.</param>
    /// <returns>A lower-case hexadecimal SHA-256 digest.</returns>
    public static string Create(string identityKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identityKey)));
    }
}
