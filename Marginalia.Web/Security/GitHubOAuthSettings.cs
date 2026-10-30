using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Marginalia.Web.Security;

/// <summary>
/// Validated GitHub OAuth and single-owner settings.
/// </summary>
public sealed class GitHubOAuthSettings
{
    /// <summary>
    /// The configuration section containing GitHub OAuth settings.
    /// </summary>
    public const string ConfigurationSection = "GitHubOAuth";

    /// <summary>
    /// The OAuth authentication scheme.
    /// </summary>
    public const string AuthenticationScheme = "GitHub";

    /// <summary>
    /// The local application-cookie scheme.
    /// </summary>
    public const string CookieScheme = "MarginaliaCookie";

    /// <summary>
    /// The authorization policy that limits access to the configured owner.
    /// </summary>
    public const string OwnerPolicy = "LibraryOwner";

    /// <summary>
    /// The authorization policy used only to permit local sign-out.
    /// </summary>
    public const string AuthenticatedPolicy = "AuthenticatedUser";

    /// <summary>
    /// The fixed local OAuth callback path.
    /// </summary>
    public const string CallbackPath = "/signin-github";

    /// <summary>
    /// The fixed local host used for OAuth.
    /// </summary>
    public const string LocalHost = "localhost";

    /// <summary>
    /// The fixed local HTTPS port used for OAuth.
    /// </summary>
    public const int LocalHttpsPort = 7144;

    /// <summary>
    /// The fixed local HTTPS callback URI used when registering the OAuth app.
    /// </summary>
    public const string LocalCallbackUri = "https://localhost:7144/signin-github";

    /// <summary>
    /// The claim containing the stable numeric GitHub user ID.
    /// </summary>
    public const string OwnerIdClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";

    /// <summary>
    /// A value that cannot match a valid positive GitHub user ID.
    /// </summary>
    public const string ConfigurationRequired = "-1";

    private GitHubOAuthSettings(string clientId, string clientSecret, string ownerId)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        OwnerId = ownerId;
    }

    /// <summary>
    /// Gets the OAuth application client ID.
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// Gets the OAuth application client secret.
    /// </summary>
    public string ClientSecret { get; }

    /// <summary>
    /// Gets the normalized numeric GitHub user ID of the sole owner.
    /// </summary>
    public string OwnerId { get; }

    /// <summary>
    /// Loads and validates the GitHub OAuth settings.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>Validated settings, or <see langword="null"/> when no values are configured.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when settings are partial or contain invalid values.
    /// </exception>
    public static GitHubOAuthSettings? Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ConfigurationSection);
        var clientId = section["ClientId"];
        var clientSecret = section["ClientSecret"];
        var ownerId = section["OwnerId"];
        var values = new[] { clientId, clientSecret, ownerId };

        if (values.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "GitHub OAuth configuration is incomplete. Provide GitHubOAuth:ClientId, GitHubOAuth:ClientSecret, and GitHubOAuth:OwnerId together.");
        }

        if (ContainsControlCharacters(clientId!) || ContainsControlCharacters(clientSecret!))
        {
            throw new InvalidOperationException("GitHub OAuth client settings contain invalid characters.");
        }

        if (!long.TryParse(ownerId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedOwnerId)
            || parsedOwnerId <= 0)
        {
            throw new InvalidOperationException("GitHubOAuth:OwnerId must be a positive numeric GitHub user ID.");
        }

        return new GitHubOAuthSettings(
            clientId!,
            clientSecret!,
            parsedOwnerId.ToString(CultureInfo.InvariantCulture));
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(char.IsControl);
}
