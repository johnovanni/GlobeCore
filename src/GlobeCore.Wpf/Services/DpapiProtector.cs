using System.Security.Cryptography;
using System.Text;
using GlobeCore.Storage;

namespace GlobeCore.Wpf.Services;

/// <summary>Windows DPAPI for the current user, with an app-specific entropy, so the blob is useless on another account.</summary>
/// <param name="entropy">The app-specific entropy string (EarthWatch3D passes "EarthWatch3D.EUMETSAT"; blobs only decrypt with the same value).</param>
public sealed class DpapiProtector(string entropy) : IProtector
{
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes(entropy);

    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), _entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedBase64)
    {
        try
        {
            var blob = Convert.FromBase64String(protectedBase64);
            if (blob.Length == 0) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, _entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
