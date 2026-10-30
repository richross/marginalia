using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Marginalia.Web.Persistence;
using Marginalia.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marginalia.Tests;

[TestClass]
public sealed class OwnerAccessTests
{
    private const string SyntheticOwnerId = "7654321";
    private const string SyntheticOtherUserId = "7654322";

    [TestMethod]
    public void MissingGitHubConfigurationIsRepresentedAsUnconfigured()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.IsNull(GitHubOAuthSettings.Load(configuration));
    }

    [TestMethod]
    public void PartialGitHubConfigurationIsRejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHubOAuth:ClientId"] = "test-client"
            })
            .Build();

        Assert.ThrowsExactly<InvalidOperationException>(() => GitHubOAuthSettings.Load(configuration));
    }

    [TestMethod]
    public void CompleteGitHubConfigurationNormalizesTheNumericOwnerId()
    {
        var configuration = CreateGitHubConfiguration("0007654321");

        var settings = GitHubOAuthSettings.Load(configuration);

        Assert.IsNotNull(settings);
        Assert.AreEqual(SyntheticOwnerId, settings.OwnerId);
        Assert.AreEqual("test-client", settings.ClientId);
    }

    [TestMethod]
    public void NonNumericOrNonPositiveGitHubOwnerIdIsRejected()
    {
        var configuration = CreateGitHubConfiguration("owner-name");

        Assert.ThrowsExactly<InvalidOperationException>(() => GitHubOAuthSettings.Load(configuration));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => GitHubOAuthSettings.Load(CreateGitHubConfiguration("0")));
    }

    [TestMethod]
    public void GitHubOAuthClientSettingsRejectControlCharacters()
    {
        var configuration = CreateGitHubConfiguration(clientSecret: "test-secret\r\ninjected");

        Assert.ThrowsExactly<InvalidOperationException>(() => GitHubOAuthSettings.Load(configuration));
    }

    [TestMethod]
    public async Task ConfiguredOwnerPolicyRequiresTheExactGitHubUserId()
    {
        using var factory = CreateFactory(injectTestAuthorization: true);
        var ownerPolicy = OwnerAccessPolicy.Create(
            GitHubOAuthSettings.Load(CreateGitHubConfiguration()));
        var owner = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, SyntheticOwnerId)],
            "unit-test"));
        var otherUser = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, SyntheticOtherUserId)],
            "unit-test"));
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();

        Assert.IsTrue((await authorization.AuthorizeAsync(owner, ownerPolicy)).Succeeded);
        Assert.IsFalse((await authorization.AuthorizeAsync(otherUser, ownerPolicy)).Succeeded);
    }

    [TestMethod]
    public async Task UnconfiguredApplicationReturnsUnauthorizedForAnonymousApiRequests()
    {
        using var factory = CreateFactory(injectTestAuthorization: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/access-check");

        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreNotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task UnconfiguredApplicationShowsPublicWelcomeAndKeepsInboxProtected()
    {
        using var factory = CreateFactory(injectTestAuthorization: false);
        using var client = factory.CreateClient();

        using var welcome = await client.GetAsync("/");
        var welcomeHtml = await welcome.Content.ReadAsStringAsync();

        Assert.AreEqual(System.Net.HttpStatusCode.OK, welcome.StatusCode);
        Assert.AreEqual("text/html", welcome.Content.Headers.ContentType?.MediaType);
        Assert.IsTrue(welcomeHtml.Contains("<h1 id=\"welcome-heading\">Keep the links you mean to come back to.</h1>", StringComparison.Ordinal));
        Assert.IsTrue(welcomeHtml.Contains("href=\"/account/login?returnUrl=%2Finbox\"", StringComparison.Ordinal));
        Assert.IsTrue(welcomeHtml.Contains("GitHub sign-in is not configured yet", StringComparison.Ordinal));

        using var inbox = await client.GetAsync("/inbox");
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, inbox.StatusCode);
    }

    [TestMethod]
    public async Task UnconfiguredApplicationDoesNotStartOAuthLogin()
    {
        using var factory = CreateFactory(injectTestAuthorization: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/account/login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsTrue(html.Contains("GitHub sign-in setup required", StringComparison.Ordinal));
        Assert.IsTrue(html.Contains("GitHubOAuth:ClientId", StringComparison.Ordinal));
        Assert.IsTrue(html.Contains("https://localhost:7144/signin-github", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AnonymousApiRequestReturnsUnauthorizedWithTestOwnerPolicy()
    {
        using var factory = CreateFactory(injectTestAuthorization: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/access-check");

        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SignedInUserOutsideOwnerIdentityIsForbidden()
    {
        using var factory = CreateFactory(injectTestAuthorization: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderTestAuthenticationHandler.IdentityHeader, "other");

        using var response = await client.GetAsync("/api/access-check");

        Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ConfiguredOwnerCanAccessProtectedApi()
    {
        using var factory = CreateFactory(injectTestAuthorization: true);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, SyntheticOwnerId)],
            "unit-test"));
        var authorization = await factory.Services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(
                principal,
                OwnerAccessPolicy.Create(GitHubOAuthSettings.Load(CreateGitHubConfiguration())));
        Assert.IsTrue(authorization.Succeeded);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderTestAuthenticationHandler.IdentityHeader, "owner");

        using var response = await client.GetAsync("/api/access-check");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GitHubChallengeRequestsNoScopesUsesPkceAndKeepsReturnUrlLocal()
    {
        using var factory = CreateFactory(injectTestAuthorization: false, configureGitHub: true);
        using var client = CreateOAuthClient(factory);

        using var response = await client.GetAsync(
            "/account/login?returnUrl=https%3A%2F%2Fattacker.example%2Fsteal");

        Assert.AreEqual(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.IsNotNull(location);
        Assert.AreEqual("github.com", location.Host);
        Assert.AreEqual("/login/oauth/authorize", location.AbsolutePath);

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.IsTrue(query.ContainsKey("state"));
        Assert.IsTrue(query.ContainsKey("code_challenge"));
        Assert.AreEqual("S256", query["code_challenge_method"].ToString());
        Assert.AreEqual(string.Empty, query["scope"].ToString());
        Assert.AreEqual(
            GitHubOAuthSettings.LocalCallbackUri,
            query["redirect_uri"].ToString());

        var oauthOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<OAuthOptions>>()
            .Get(GitHubOAuthSettings.AuthenticationScheme);
        var properties = oauthOptions.StateDataFormat!.Unprotect(query["state"].ToString());
        Assert.IsNotNull(properties);
        Assert.AreEqual("/", properties.RedirectUri);
    }

    [TestMethod]
    public void GitHubOAuthUsesAnIdentityOnlyCookieAndDoesNotSaveProviderTokens()
    {
        using var factory = CreateFactory(injectTestAuthorization: false, configureGitHub: true);
        Assert.AreEqual(
            "test-client",
            factory.Services.GetRequiredService<IConfiguration>()["GitHubOAuth:ClientId"]);
        var cookie = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(GitHubOAuthSettings.CookieScheme);
        var oauth = factory.Services
            .GetRequiredService<IOptionsMonitor<OAuthOptions>>()
            .Get(GitHubOAuthSettings.AuthenticationScheme);

        Assert.AreEqual("__Host-Marginalia.Auth", cookie.Cookie.Name);
        Assert.AreEqual(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.IsTrue(cookie.Cookie.HttpOnly);
        Assert.AreEqual(SameSiteMode.Lax, cookie.Cookie.SameSite);
        Assert.IsFalse(cookie.SlidingExpiration);
        Assert.IsFalse(oauth.SaveTokens);
        Assert.IsEmpty(oauth.Scope);
        Assert.IsTrue(oauth.UsePkce);
        Assert.AreEqual(0, oauth.ClaimActions.Count());
    }

    [TestMethod]
    public async Task OAuthCallbackFailureReturnsSafeLocalErrorWithoutIssuingAppCookie()
    {
        using var factory = CreateFactory(injectTestAuthorization: false, configureGitHub: true);
        using var client = CreateOAuthClient(factory);

        using var response = await client.GetAsync("/signin-github?error=access_denied");

        Assert.AreEqual(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/account/sign-in-failed", response.Headers.Location?.OriginalString);
        Assert.IsFalse(
            response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith("__Host-Marginalia.Auth=", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task GitHubCallbackCreatesOwnerSessionUsingOnlyTheNumericIdentity()
    {
        using var factory = CreateFactory(
            injectTestAuthorization: false,
            configureGitHub: true,
            fakeGitHubUserId: SyntheticOwnerId);
        var fakeBackchannel = factory.Services
            .GetRequiredService<IOptionsMonitor<OAuthOptions>>()
            .Get(GitHubOAuthSettings.AuthenticationScheme)
            .BackchannelHttpHandler as FakeGitHubBackchannelHandler
            ?? throw new InvalidOperationException("The synthetic GitHub backchannel was not configured.");
        using var client = CreateOAuthClient(factory);

        var challenge = await client.GetAsync("/account/login");
        var challengeQuery = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        var callbackUrl = QueryHelpers.AddQueryString(
            GitHubOAuthSettings.CallbackPath,
            new Dictionary<string, string?>
            {
                ["code"] = "synthetic-code",
                ["state"] = challengeQuery["state"].ToString()
            });

        using var callback = await client.GetAsync(callbackUrl);
        Assert.AreEqual(System.Net.HttpStatusCode.Redirect, callback.StatusCode);
        Assert.AreEqual("/", callback.Headers.Location?.OriginalString);
        Assert.Contains("/user", fakeBackchannel.RequestPaths);
        Assert.Contains("Bearer", fakeBackchannel.AuthorizationSchemes);
        var authCookie = callback.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith("__Host-Marginalia.Auth=", StringComparison.Ordinal));
        var protectedTicket = authCookie.Split(';', 2)[0].Split('=', 2)[1];
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(GitHubOAuthSettings.CookieScheme);
        var ticket = cookieOptions.TicketDataFormat.Unprotect(protectedTicket);
        Assert.IsNotNull(ticket);
        Assert.IsTrue(
            string.Equals(
                SyntheticOwnerId,
                ticket.Principal.FindFirst(GitHubOAuthSettings.OwnerIdClaim)?.Value,
                StringComparison.Ordinal),
            "The app cookie should contain only the validated GitHub numeric owner identity.");

        using var api = await client.GetAsync("/api/access-check");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, api.StatusCode);
    }

    [TestMethod]
    public async Task GitHubCallbackRejectsWrongUserBeforeIssuingOwnerSession()
    {
        using var factory = CreateFactory(
            injectTestAuthorization: false,
            configureGitHub: true,
            fakeGitHubUserId: SyntheticOtherUserId);
        using var client = CreateOAuthClient(factory);

        var challenge = await client.GetAsync("/account/login");
        var challengeQuery = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        var callbackUrl = QueryHelpers.AddQueryString(
            GitHubOAuthSettings.CallbackPath,
            new Dictionary<string, string?>
            {
                ["code"] = "synthetic-code",
                ["state"] = challengeQuery["state"].ToString()
            });

        using var callback = await client.GetAsync(callbackUrl);
        Assert.AreEqual(System.Net.HttpStatusCode.Redirect, callback.StatusCode);
        Assert.AreEqual("/account/sign-in-failed", callback.Headers.Location?.OriginalString);

        using var api = await client.GetAsync("/api/access-check");
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [TestMethod]
    public async Task GitHubCallbackRejectsOversizedChunkedIdentityResponse()
    {
        using var factory = CreateFactory(
            injectTestAuthorization: false,
            configureGitHub: true,
            fakeGitHubUserId: SyntheticOwnerId,
            fakeGitHubProfileTooLarge: true);
        using var client = CreateOAuthClient(factory);

        var challenge = await client.GetAsync("/account/login");
        var challengeQuery = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        var callbackUrl = QueryHelpers.AddQueryString(
            GitHubOAuthSettings.CallbackPath,
            new Dictionary<string, string?>
            {
                ["code"] = "synthetic-code",
                ["state"] = challengeQuery["state"].ToString()
            });

        using var callback = await client.GetAsync(callbackUrl);
        Assert.AreEqual(System.Net.HttpStatusCode.Redirect, callback.StatusCode);
        Assert.AreEqual("/account/sign-in-failed", callback.Headers.Location?.OriginalString);
        Assert.IsFalse(
            callback.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith("__Host-Marginalia.Auth=", StringComparison.Ordinal)));

        using var api = await client.GetAsync("/api/access-check");
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [TestMethod]
    public async Task OAuthLoginAndCallbackRejectNonLocalOrigins()
    {
        using var factory = CreateFactory(injectTestAuthorization: false, configureGitHub: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://attacker.example:7144"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/account/login");
        using var callbackResponse = await client.GetAsync("/signin-github?error=access_denied");

        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, callbackResponse.StatusCode);
    }

    [TestMethod]
    public async Task LegacyEntraCookieIsExpiredButNotAcceptedAsGitHubSession()
    {
        using var factory = CreateFactory(injectTestAuthorization: false, configureGitHub: true);
        using var client = CreateOAuthClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Cookies=legacy-ticket");

        using var response = await client.GetAsync("/api/access-check");

        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsTrue(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.IsTrue(cookies!.Any(cookie => cookie.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)));
        Assert.IsFalse(cookies.Any(cookie => cookie.StartsWith("__Host-Marginalia.Auth=", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task LogoutRequiresAntiforgeryValidation()
    {
        using var factory = CreateFactory(injectTestAuthorization: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderTestAuthenticationHandler.IdentityHeader, "owner");

        using var response = await client.PostAsync("/account/logout", content: null);

        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PublicPwaShellAssetsDoNotExposeProtectedApplicationRoutes()
    {
        using var factory = CreateFactory(injectTestAuthorization: false);
        using var client = factory.CreateClient();

        foreach (var asset in new[]
                 {
                     "/manifest.webmanifest",
                     "/service-worker.js",
                     "/app.css",
                     "/welcome.css",
                     "/welcome.html",
                     "/offline.html",
                     "/icon-192.png"
                 })
        {
            using var response = await client.GetAsync(asset);
            Assert.AreEqual(
                System.Net.HttpStatusCode.OK,
                response.StatusCode,
                $"The public PWA shell asset '{asset}' should not require owner authentication.");
        }

        using var page = await client.GetAsync("/");
        using var inbox = await client.GetAsync("/inbox");
        using var api = await client.GetAsync("/api/access-check");
        using var export = await client.GetAsync("/api/library/export.json");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, page.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, inbox.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, export.StatusCode);
    }

    [TestMethod]
    public async Task ConfiguredTestOwnerCanRenderAllPrimaryRoutes()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MARGINALIA_TEST_CONNECTION_STRING")))
        {
            Assert.Inconclusive("Set the isolated local PostgreSQL test connection to render database-backed routes.");
        }

        using var factory = CreateFactory(injectTestAuthorization: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderTestAuthenticationHandler.IdentityHeader, "owner");

        var routes = new (string Path, string Heading)[]
        {
            ("/inbox", "<h1>Inbox</h1>"),
            ("/library", "<h1>Saved library</h1>"),
            ("/capture", "<h1>Capture a URL</h1>"),
            ("/feeds", "<h1>Feeds</h1>"),
            ("/organization", "<h1>Organization</h1>"),
            ("/recovery", "<h2 id=\"recovery-heading\">Dismissed items</h2>"),
            ("/settings", "<h1>Settings</h1>")
        };

        foreach (var (path, heading) in routes)
        {
            using var response = await client.GetAsync(path);
            Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode, $"Route '{path}' should render for the configured test owner.");
            var html = await response.Content.ReadAsStringAsync();
            Assert.IsTrue(html.Contains(heading, StringComparison.Ordinal), $"Route '{path}' should contain its expected heading.");
            if (path == "/settings")
            {
                Assert.IsTrue(html.Contains("library-import-file", StringComparison.Ordinal));
                Assert.IsTrue(html.Contains("Import Saved links", StringComparison.Ordinal));
                Assert.IsTrue(html.Contains("configured GitHub account", StringComparison.Ordinal));
            }
            if (path == "/inbox")
            {
                Assert.IsTrue(html.Contains("account/logout", StringComparison.Ordinal));
                Assert.IsTrue(html.Contains("__RequestVerificationToken", StringComparison.Ordinal));
            }
        }

        using var exportResponse = await client.GetAsync("/api/library/export.json");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.AreEqual("application/json", exportResponse.Content.Headers.ContentType?.MediaType);
    }

    private static IConfiguration CreateGitHubConfiguration(
        string ownerId = SyntheticOwnerId,
        string clientSecret = "test-client-secret") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHubOAuth:ClientId"] = "test-client",
                ["GitHubOAuth:ClientSecret"] = clientSecret,
                ["GitHubOAuth:OwnerId"] = ownerId
            })
            .Build();

    private static HttpClient CreateOAuthClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(
                new Uri(GitHubOAuthSettings.LocalCallbackUri).GetLeftPart(UriPartial.Authority)),
            AllowAutoRedirect = false
        });

    private static WebApplicationFactory<Program> CreateFactory(
        bool injectTestAuthorization,
        bool configureGitHub = false,
        string? fakeGitHubUserId = null,
        bool fakeGitHubProfileTooLarge = false) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                var testConnectionString = Environment.GetEnvironmentVariable("MARGINALIA_TEST_CONNECTION_STRING");
                var testSettings = new Dictionary<string, string?>();
                if (!string.IsNullOrWhiteSpace(testConnectionString))
                {
                    testSettings["ConnectionStrings:marginalia"] = testConnectionString;
                }

                if (configureGitHub)
                {
                    testSettings["GitHubOAuth:ClientId"] = "test-client";
                    testSettings["GitHubOAuth:ClientSecret"] = "test-client-secret";
                    testSettings["GitHubOAuth:OwnerId"] = SyntheticOwnerId;
                }

                foreach (var (key, value) in testSettings)
                {
                    if (value is not null)
                    {
                        builder.UseSetting(key, value);
                    }
                }

                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(testSettings));

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.RemoveAll<IDatabaseInitializer>();
                    services.AddSingleton<IDatabaseInitializer, NoOpDatabaseInitializer>();

                    if (injectTestAuthorization)
                    {
                        services.AddAuthentication(options =>
                            {
                                options.DefaultAuthenticateScheme = HeaderTestAuthenticationHandler.SchemeName;
                                options.DefaultChallengeScheme = HeaderTestAuthenticationHandler.SchemeName;
                                options.DefaultScheme = HeaderTestAuthenticationHandler.SchemeName;
                            })
                            .AddScheme<AuthenticationSchemeOptions, HeaderTestAuthenticationHandler>(
                                HeaderTestAuthenticationHandler.SchemeName,
                                _ => { });
                        services.Configure<AuthorizationOptions>(options =>
                        {
                            var testSettings = GitHubOAuthSettings.Load(CreateGitHubConfiguration());
                            var ownerPolicy = OwnerAccessPolicy.Create(testSettings);
                            options.AddPolicy(GitHubOAuthSettings.OwnerPolicy, ownerPolicy);
                            options.FallbackPolicy = ownerPolicy;
                        });
                    }

                    if (fakeGitHubUserId is not null)
                    {
                        services.PostConfigure<OAuthOptions>(
                            GitHubOAuthSettings.AuthenticationScheme,
                            options =>
                            {
                                var fakeBackchannel = new FakeGitHubBackchannelHandler(
                                    fakeGitHubUserId,
                                    fakeGitHubProfileTooLarge);
                                options.BackchannelHttpHandler = fakeBackchannel;
                                options.Backchannel = new HttpClient(fakeBackchannel);
                            });
                    }
                });
            });

    private sealed class NoOpDatabaseInitializer : IDatabaseInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class HeaderTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "MarginaliaTest";
        public const string IdentityHeader = "X-Test-Identity";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(IdentityHeader, out var identity))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var userId = string.Equals(identity.ToString(), "owner", StringComparison.Ordinal)
                ? SyntheticOwnerId
                : SyntheticOtherUserId;
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakeGitHubBackchannelHandler(
        string githubUserId,
        bool profileTooLarge = false) : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = [];

        public List<string> AuthorizationSchemes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            if (request.Headers.Authorization?.Scheme is { } authorizationScheme)
            {
                AuthorizationSchemes.Add(authorizationScheme);
            }

            if (request.RequestUri?.AbsolutePath == "/user" && profileTooLarge)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StreamContent(new NonSeekableMemoryStream(
                        Encoding.UTF8.GetBytes(new string('x', 16 * 1024 + 1))))
                });
            }

            var responseBody = request.RequestUri?.AbsolutePath switch
            {
                "/login/oauth/access_token" =>
                    """{"access_token":"synthetic-oauth-token","token_type":"bearer","scope":""}""",
                "/user" =>
                    $$"""{"id":{{githubUserId}},"login":"synthetic-user","email":"synthetic@example.test"}""",
                _ => throw new InvalidOperationException("Unexpected GitHub OAuth backchannel request.")
            };

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class NonSeekableMemoryStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
