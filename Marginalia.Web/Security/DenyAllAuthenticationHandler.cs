using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Marginalia.Web.Security;

/// <summary>
/// Rejects all authentication when GitHub owner configuration is absent.
/// </summary>
public sealed class DenyAllAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// The authentication scheme used until GitHub OAuth is fully configured.
    /// </summary>
    public const string SchemeName = "MarginaliaDenyAll";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.Fail("GitHub owner configuration is required."));
}
