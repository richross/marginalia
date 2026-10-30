using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Marginalia.Web.Security;

/// <summary>
/// Returns API authorization failures as HTTP statuses instead of interactive redirects.
/// </summary>
public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    /// <inheritdoc />
    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && (authorizeResult.Challenged || authorizeResult.Forbidden))
        {
            context.Response.StatusCode = authorizeResult.Forbidden
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        return _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
