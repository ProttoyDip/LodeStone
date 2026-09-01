namespace Lodestone.Application.Interfaces;

/// <summary>Protects sensitive application text before persistence.</summary>
public interface ISensitiveDataProtector
{
    bool IsProtected(string value);
    string Protect(string plaintext);
    string Unprotect(string protectedOrLegacyValue);
}
