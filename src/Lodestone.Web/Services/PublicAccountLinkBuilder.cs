using Lodestone.Web.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Lodestone.Web.Services;

public interface IPublicAccountLinkBuilder
{
    string BuildPasswordResetUrl(string email, string encodedToken);
}

/// <summary>
/// Builds account-recovery links from the configured public origin only.
/// </summary>
public sealed class PublicAccountLinkBuilder : IPublicAccountLinkBuilder
{
    private readonly Uri _baseUri;

    public PublicAccountLinkBuilder(IOptions<PublicUrlSettings> settings)
    {
        if (!PublicUrlSettings.TryGetBaseUri(settings.Value.BaseUrl, out _baseUri))
        {
            throw new OptionsValidationException(
                PublicUrlSettings.SectionName,
                typeof(PublicUrlSettings),
                ["PublicUrl:BaseUrl must be an absolute HTTPS origin or path base without credentials, query, or fragment."]);
        }
    }

    public string BuildPasswordResetUrl(string email, string encodedToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedToken);

        var basePath = _baseUri.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(_baseUri)
        {
            Path = $"{basePath}/Account/ResetPassword",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return QueryHelpers.AddQueryString(
            builder.Uri.AbsoluteUri,
            new Dictionary<string, string?>
            {
                ["email"] = email,
                ["token"] = encodedToken
            });
    }
}
