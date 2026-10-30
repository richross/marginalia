using Microsoft.AspNetCore.Authorization;

namespace Marginalia.Web.Security;

/// <summary>
/// Builds the fail-closed authorization policies for the owner and local sign-out.
/// </summary>
public static class OwnerAccessPolicy
{
    /// <summary>
    /// Creates a policy requiring the exact configured GitHub user ID.
    /// </summary>
    /// <param name="settings">Validated GitHub settings, or <see langword="null"/> when unconfigured.</param>
    /// <returns>A policy that denies access unless the configured owner is authenticated.</returns>
    public static AuthorizationPolicy Create(GitHubOAuthSettings? settings) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(
                GitHubOAuthSettings.OwnerIdClaim,
                settings?.OwnerId ?? GitHubOAuthSettings.ConfigurationRequired)
            .Build();
}
