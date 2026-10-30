using System.Net;
using System.Net.Http.Headers;
using Marginalia.Web.Application;
using Marginalia.Web.Security;

namespace Marginalia.Tests;

[TestClass]
public sealed class SafePublicHttpFetcherTests
{
    [TestMethod]
    public void PublicAddressPolicyAllowsPublicAddressesAndRejectsReservedRanges()
    {
        var publicAddresses = new[]
        {
            "8.8.8.8",
            "1.1.1.1",
            "2001:4860:4860::8888"
        };
        var blockedAddresses = new[]
        {
            "0.1.2.3",
            "10.0.0.1",
            "100.64.0.1",
            "127.0.0.1",
            "169.254.1.1",
            "172.20.1.1",
            "192.0.2.1",
            "192.168.1.1",
            "198.18.0.1",
            "203.0.113.1",
            "224.0.0.1",
            "255.255.255.255",
            "::1",
            "fc00::1",
            "fe80::1",
            "2001:db8::1",
            "2002::1",
            "3fff::1"
        };

        foreach (var address in publicAddresses)
        {
            Assert.IsTrue(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)), $"Expected {address} to be public.");
        }

        foreach (var address in blockedAddresses)
        {
            Assert.IsFalse(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)), $"Expected {address} to be blocked.");
        }
    }

    [TestMethod]
    public void HtmlMetadataParserExtractsOnlyBoundedPlainTextFields()
    {
        var metadata = HtmlMetadataParser.Extract(
            "<html><head><title>Fallback &amp; title</title>" +
            "<meta name='description' content='A &amp; B'>" +
            "<meta property=og:title content='Preferred &amp; title'></head></html>");

        Assert.AreEqual("Preferred & title", metadata.Title);
        Assert.AreEqual("A & B", metadata.Description);

        var hostile = HtmlMetadataParser.Extract(
            "<title><script>alert('not stored')</script> Safe title</title>" +
            "<meta name=description content='&lt;img src=x onerror=alert(1)&gt; text'>");
        Assert.AreEqual("alert('not stored') Safe title", hostile.Title);
        Assert.AreEqual("text", hostile.Description);
    }

    [TestMethod]
    public async Task FetchRejectsPrivateDnsAnswersBeforeSendingRequest()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = CreateClient(handler);
        var resolver = new FixedHostAddressResolver(IPAddress.Loopback);
        var fetcher = new SafePublicHttpFetcher(client, resolver);

        var exception = await Assert.ThrowsExactlyAsync<SafeHttpFetchException>(() =>
            fetcher.FetchAsync(new Uri("https://feeds.example.test/feed.xml")));

        Assert.AreEqual(SafeHttpFetchFailure.DnsRejected, exception.Failure);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task ConnectionTimeDnsRebindingIsRejectedBeforeOpeningSocket()
    {
        var resolver = new SequenceHostAddressResolver(
            IPAddress.Parse("8.8.8.8"),
            IPAddress.Loopback);
        using var client = new HttpClient(PublicHttpMessageHandlerFactory.Create(resolver))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var fetcher = new SafePublicHttpFetcher(client, resolver);

        var exception = await Assert.ThrowsExactlyAsync<SafeHttpFetchException>(() =>
            fetcher.FetchAsync(new Uri("http://rebind.example.test/feed.xml")));

        Assert.AreEqual(SafeHttpFetchFailure.NetworkFailure, exception.Failure);
        Assert.AreEqual(2, resolver.ResolutionCount);
    }

    [TestMethod]
    public async Task FetchRevalidatesRedirectAndDoesNotContactPrivateAddress()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://127.0.0.1/private") }
            }));
        using var client = CreateClient(handler);
        var resolver = new FixedHostAddressResolver(IPAddress.Parse("8.8.8.8"));
        var fetcher = new SafePublicHttpFetcher(client, resolver);

        var exception = await Assert.ThrowsExactlyAsync<SafeHttpFetchException>(() =>
            fetcher.FetchAsync(new Uri("https://feeds.example.test/feed.xml")));

        Assert.AreEqual(SafeHttpFetchFailure.RedirectRejected, exception.Failure);
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task FetchRejectsUnsafeSchemesCredentialsAndNonstandardPorts()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = CreateClient(handler);
        var resolver = new FixedHostAddressResolver(IPAddress.Parse("8.8.8.8"));
        var fetcher = new SafePublicHttpFetcher(client, resolver);

        foreach (var target in new[]
                 {
                     new Uri("file:///C:/private.txt"),
                     new Uri("http://user:password@feeds.example.test/"),
                     new Uri("https://feeds.example.test:8443/feed.xml"),
                     new Uri("http://feeds.example.test:443/feed.xml")
                 })
        {
            var exception = await Assert.ThrowsExactlyAsync<SafeHttpFetchException>(() =>
                fetcher.FetchAsync(target));
            Assert.AreEqual(SafeHttpFetchFailure.UnsafeTarget, exception.Failure);
        }

        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task FetchEnforcesMaximumResponseSize()
    {
        var content = new ByteArrayContent(new byte[4 * 1024 * 1024 + 1]);
        content.Headers.ContentLength = null;
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var client = CreateClient(handler);
        var fetcher = new SafePublicHttpFetcher(
            client,
            new FixedHostAddressResolver(IPAddress.Parse("8.8.8.8")));

        var exception = await Assert.ThrowsExactlyAsync<SafeHttpFetchException>(() =>
            fetcher.FetchAsync(new Uri("https://feeds.example.test/feed.xml")));

        Assert.AreEqual(SafeHttpFetchFailure.ResponseTooLarge, exception.Failure);
    }

    [TestMethod]
    public async Task FetchSendsConditionalValidatorsAndAcceptsNotModified()
    {
        var lastModified = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.AreEqual("W/\"feed-version\"", request.Headers.IfNoneMatch.Single().ToString());
            Assert.AreEqual(lastModified, request.Headers.IfModifiedSince);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                Headers = { ETag = new EntityTagHeaderValue("\"feed-version\"") }
            });
        });
        using var client = CreateClient(handler);
        var fetcher = new SafePublicHttpFetcher(
            client,
            new FixedHostAddressResolver(IPAddress.Parse("8.8.8.8")));

        var response = await fetcher.FetchAsync(
            new Uri("https://feeds.example.test/feed.xml"),
            "W/\"feed-version\"",
            lastModified);

        Assert.AreEqual(HttpStatusCode.NotModified, response.StatusCode);
        Assert.AreEqual("\"feed-version\"", response.EntityTag);
        Assert.IsEmpty(response.Body);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { Timeout = Timeout.InfiniteTimeSpan };

    private sealed class FixedHostAddressResolver(params IPAddress[] addresses) : IHostAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addresses);
    }

    private sealed class SequenceHostAddressResolver(params IPAddress[] addresses) : IHostAddressResolver
    {
        public int ResolutionCount { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            var address = addresses[Math.Min(ResolutionCount, addresses.Length - 1)];
            ResolutionCount++;
            return Task.FromResult<IPAddress[]>([address]);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return send(request, cancellationToken);
        }
    }
}
