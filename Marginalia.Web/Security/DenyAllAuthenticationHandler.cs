using System.Text;
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

    /// <inheritdoc />
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "text/html; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        await Response.WriteAsync(
            """
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Sign-in setup required · Marginalia</title>
              <style>
                :root { color-scheme: light; font-family: "Segoe UI", sans-serif; color: #173247; background: #f7f5ef; }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; }
                main { box-sizing: border-box; width: min(42rem, calc(100% - 2rem)); padding: 2rem; border: 1px solid #d6d3c9; border-top: 4px solid #c69b26; background: #fffefa; }
                h1 { margin-top: 0; font-size: clamp(1.7rem, 5vw, 2.4rem); }
                p, li { line-height: 1.6; }
                code { overflow-wrap: anywhere; }
              </style>
            </head>
            <body>
              <main>
                <h1>GitHub sign-in setup required</h1>
                <p>Marginalia is running, but owner sign-in is not configured yet. Protected library pages remain unavailable until the GitHub OAuth settings are supplied.</p>
                <p>After registering the local GitHub OAuth App, set <code>GitHubOAuth:ClientId</code>, <code>GitHubOAuth:ClientSecret</code>, and <code>GitHubOAuth:OwnerId</code> with .NET user-secrets, then restart the web resource.</p>
                <p>OAuth callback URL: <code>https://localhost:7144/signin-github</code></p>
                <p>See the README section “GitHub owner authentication” for the setup steps. API requests remain unauthorized until the owner is configured.</p>
              </main>
            </body>
            </html>
            """,
            Encoding.UTF8,
            Context.RequestAborted);
    }
}
