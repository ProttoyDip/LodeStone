using Lodestone.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace Lodestone.Infrastructure.Security;

/// <summary>Protects sensitive fields (e.g. journal notes) using ASP.NET Data Protection.</summary>
public sealed class DataProtectionService : ISensitiveDataProtector
{
    private const string ProtectedValuePrefix = "lodestone:protected:v1:";
    private readonly IDataProtector _protector;

    public DataProtectionService(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Lodestone.Sensitive.v1");

    public bool IsProtected(string value)
        => value.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedValuePrefix + _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedOrLegacyValue)
    {
        ArgumentNullException.ThrowIfNull(protectedOrLegacyValue);
        if (!IsProtected(protectedOrLegacyValue))
        {
            return protectedOrLegacyValue;
        }

        try
        {
            return _protector.Unprotect(protectedOrLegacyValue[ProtectedValuePrefix.Length..]);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "Sensitive protected data could not be decrypted with the configured key ring.",
                exception);
        }
    }
}
