using Marginalia.Web.Components;
using Marginalia.Web.Application;
using Marginalia.Web.Persistence;
using Marginalia.Web.Security;
using System.Globalization;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<LibraryDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("marginalia");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "The PostgreSQL connection string 'marginalia' must be supplied by the Aspire AppHost.");
    }

    options.UseNpgsql(connectionString);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
builder.Services.AddScoped<LibraryCommandService>();
builder.Services.AddScoped<LibraryQueryService>();
builder.Services.AddScoped<LibraryExportService>();
builder.Services.AddScoped<LibraryImportService>();
builder.Services.AddScoped<ManualUrlCaptureService>();
builder.Services.AddScoped<FeedSubscriptionService>();
builder.Services.AddScoped<FeedIngestionService>();
builder.Services.AddScoped<FeedPollingService>();
builder.Services.AddSingleton<IHostAddressResolver, DnsHostAddressResolver>();
builder.Services.AddSingleton<ISafePublicHttpFetcher>(serviceProvider =>
{
    var resolver = serviceProvider.GetRequiredService<IHostAddressResolver>();
    var client = new HttpClient(PublicHttpMessageHandlerFactory.Create(resolver))
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    return new SafePublicHttpFetcher(client, resolver);
});
builder.Services.AddSingleton<FeedRefreshGate>();
builder.Services.AddScoped<ArticleRetentionService>();
builder.Services.AddHostedService<RetentionCleanupService>();
builder.Services.AddHostedService<FeedPollingWorker>();

var githubSettings = GitHubOAuthSettings.Load(builder.Configuration);
var ownerPolicy = OwnerAccessPolicy.Create(githubSettings);

if (githubSettings is null)
{
    builder.Services.AddAuthentication(DenyAllAuthenticationHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
            DenyAllAuthenticationHandler>(DenyAllAuthenticationHandler.SchemeName, _ => { });
}
else
{
    builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = GitHubOAuthSettings.CookieScheme;
            options.DefaultSignInScheme = GitHubOAuthSettings.CookieScheme;
            options.DefaultChallengeScheme = GitHubOAuthSettings.CookieScheme;
            options.DefaultForbidScheme = GitHubOAuthSettings.CookieScheme;
        })
        .AddCookie(GitHubOAuthSettings.CookieScheme, options =>
        {
            options.Cookie.Name = "__Host-Marginalia.Auth";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.IsEssential = true;
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/account/denied";
            options.ReturnUrlParameter = "returnUrl";
            options.SlidingExpiration = false;
        })
        .AddOAuth(GitHubOAuthSettings.AuthenticationScheme, options =>
        {
            options.SignInScheme = GitHubOAuthSettings.CookieScheme;
            options.ClientId = githubSettings.ClientId;
            options.ClientSecret = githubSettings.ClientSecret;
            options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
            options.TokenEndpoint = "https://github.com/login/oauth/access_token";
            options.UserInformationEndpoint = "https://api.github.com/user";
            options.CallbackPath = GitHubOAuthSettings.CallbackPath;
            options.SaveTokens = false;
            options.UsePkce = true;
            options.Scope.Clear();
            options.ClaimActions.Clear();
            options.BackchannelHttpHandler = new GitHubUserAgentHandler();
            options.BackchannelTimeout = TimeSpan.FromSeconds(30);
            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        context.Options.UserInformationEndpoint);
                    request.Headers.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", context.AccessToken);

                    using var profileTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                        context.HttpContext.RequestAborted);
                    profileTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    using var response = await context.Backchannel.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        profileTimeout.Token);
                    if (!response.IsSuccessStatusCode
                        || response.Content.Headers.ContentLength is > 16 * 1024)
                    {
                        throw new AuthenticationFailureException(
                            "GitHub did not return a valid public identity.");
                    }

                    await using var profileStream =
                        await response.Content.ReadAsStreamAsync(profileTimeout.Token);
                    using var profileBuffer = new MemoryStream();
                    var chunk = new byte[4096];
                    while (true)
                    {
                        var bytesRead = await profileStream.ReadAsync(chunk, profileTimeout.Token);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        if (profileBuffer.Length + bytesRead > 16 * 1024)
                        {
                            throw new AuthenticationFailureException(
                                "GitHub did not return a valid public identity.");
                        }

                        await profileBuffer.WriteAsync(chunk.AsMemory(0, bytesRead), profileTimeout.Token);
                    }

                    using var user = JsonDocument.Parse(profileBuffer.ToArray());
                    var profile = user.RootElement;
                    if (!profile.TryGetProperty("id", out var idElement)
                        || idElement.ValueKind != JsonValueKind.Number
                        || !idElement.TryGetInt64(out var userId)
                        || userId <= 0
                        || !string.Equals(
                            userId.ToString(CultureInfo.InvariantCulture),
                            githubSettings.OwnerId,
                            StringComparison.Ordinal))
                    {
                        throw new AuthenticationFailureException(
                            "The authenticated GitHub account is not the configured library owner.");
                    }

                    if (context.Principal is null)
                    {
                        throw new AuthenticationFailureException("GitHub did not return a valid identity.");
                    }

                    context.Principal.AddIdentity(new ClaimsIdentity(
                        [new Claim(
                            GitHubOAuthSettings.OwnerIdClaim,
                            userId.ToString(CultureInfo.InvariantCulture))],
                        GitHubOAuthSettings.AuthenticationScheme));
                },
                OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/account/sign-in-failed");
                    return Task.CompletedTask;
                }
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(GitHubOAuthSettings.OwnerPolicy, ownerPolicy);
    options.AddPolicy(
        GitHubOAuthSettings.AuthenticatedPolicy,
        policy => policy.RequireAuthenticatedUser());
    options.FallbackPolicy = ownerPolicy;
});
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/account")
        && !context.Request.Path.StartsWithSegments(GitHubOAuthSettings.CallbackPath),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    if (context.Request.Cookies.ContainsKey(".AspNetCore.Cookies"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Cookies.Delete(
                ".AspNetCore.Cookies",
                new CookieOptions
                {
                    Path = "/",
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax
                });
            return Task.CompletedTask;
        });
    }

    var requestPath = context.Request.Path.Value;
    if (githubSettings is not null
        && (string.Equals(requestPath, "/account/login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requestPath, GitHubOAuthSettings.CallbackPath, StringComparison.OrdinalIgnoreCase))
        && (!context.Request.IsHttps
            || !string.Equals(context.Request.Host.Host, GitHubOAuthSettings.LocalHost, StringComparison.OrdinalIgnoreCase)
            || context.Request.Host.Port != GitHubOAuthSettings.LocalHttpsPort))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("OAuth is available only at the fixed local HTTPS origin.");
        return;
    }

    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

await app.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

app.MapDefaultEndpoints();
app.MapGet("/account/login", (HttpContext context) =>
    {
        if (githubSettings is null)
        {
            return Results.Problem(
                "GitHub owner authentication is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var returnUrl = LocalReturnUrl.Normalize(context.Request.Query["returnUrl"].ToString());
        var properties = new AuthenticationProperties { RedirectUri = returnUrl };
        return Results.Challenge(properties, [GitHubOAuthSettings.AuthenticationScheme]);
    })
    .AllowAnonymous();
app.MapGet("/account/sign-in-failed", () =>
        Results.Problem(
            "GitHub sign-in could not be completed. Start sign-in again.",
            statusCode: StatusCodes.Status401Unauthorized))
    .AllowAnonymous();
app.MapGet("/account/denied", () =>
        Results.Content(
            "This GitHub account is not authorized to use this library.",
            "text/plain; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden))
    .AllowAnonymous();
app.MapGet("/account/signed-out", () =>
        Results.Content(
            "You are signed out. <a href=\"/account/login\">Sign in</a>.",
            "text/html; charset=utf-8"))
    .AllowAnonymous();
app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        await context.SignOutAsync(GitHubOAuthSettings.CookieScheme);
        return Results.Redirect("/account/signed-out");
    })
    .RequireAuthorization(GitHubOAuthSettings.AuthenticatedPolicy);
app.MapGet("/api/access-check", () => Results.Ok(new { access = "owner" }))
    .RequireAuthorization(GitHubOAuthSettings.OwnerPolicy);
app.MapGet("/api/library/export.json", async (
        LibraryExportService exportService,
        CancellationToken cancellationToken) =>
    {
        var json = await exportService.ExportJsonAsync(cancellationToken);
        var fileName = $"marginalia-library-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}Z.json";
        return Results.File(json, "application/json; charset=utf-8", fileName);
    })
    .RequireAuthorization(GitHubOAuthSettings.OwnerPolicy);
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(GitHubOAuthSettings.OwnerPolicy);

await app.RunAsync();

public partial class Program
{
}
