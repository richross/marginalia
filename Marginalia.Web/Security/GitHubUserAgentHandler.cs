namespace Marginalia.Web.Security;

internal sealed class GitHubUserAgentHandler : DelegatingHandler
{
    public GitHubUserAgentHandler()
        : base(new SocketsHttpHandler())
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.UserAgent.ParseAdd("Marginalia");
        return base.SendAsync(request, cancellationToken);
    }
}
