namespace Lodestone.Web.Configuration;

/// <summary>
/// The canonical public origin used in security-sensitive links sent by email.
/// It is intentionally independent of request headers so a forged Host header
/// cannot turn a password-reset or account-setup link into an attacker URL.
/// </summary>
public sealed class PublicUrlSettings
{
    public const string SectionName = "PublicUrl";

    public string BaseUrl { get; set; } = string.Empty;

    public static bool IsValid(PublicUrlSettings settings)
        => TryGetBaseUri(settings.BaseUrl, out _);

    public static bool TryGetBaseUri(string? value, out Uri baseUri)
    {
        baseUri = null!;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        baseUri = parsed;
        return true;
    }
}
