using System.Net;
using System.Net.Sockets;

namespace Marginalia.Web.Security;

/// <summary>
/// Creates an HTTP transport that pins each connection to validated public DNS results.
/// </summary>
public static class PublicHttpMessageHandlerFactory
{
    /// <summary>
    /// Creates a handler that disables implicit redirects, cookies, and proxy routing.
    /// </summary>
    /// <param name="addressResolver">The resolver used at connection time.</param>
    /// <returns>A bounded transport configured for explicit redirect validation.</returns>
    public static SocketsHttpHandler Create(IHostAddressResolver addressResolver)
    {
        ArgumentNullException.ThrowIfNull(addressResolver);

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectCallback = (context, cancellationToken) =>
                ConnectToValidatedAddressAsync(context, addressResolver, cancellationToken),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 8,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseCookies = false,
            UseProxy = false
        };
    }

    private static async ValueTask<Stream> ConnectToValidatedAddressAsync(
        SocketsHttpConnectionContext context,
        IHostAddressResolver addressResolver,
        CancellationToken cancellationToken)
    {
        var addresses = await addressResolver.ResolveAsync(context.DnsEndPoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !PublicAddressPolicy.IsPublic(address)))
        {
            throw new SocketException((int)SocketError.AccessDenied);
        }

        SocketException? lastFailure = null;
        foreach (var address in addresses)
        {
            var client = new TcpClient(address.AddressFamily) { NoDelay = true };
            try
            {
                await client.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return client.GetStream();
            }
            catch (SocketException exception)
            {
                client.Dispose();
                lastFailure = exception;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        throw lastFailure ?? new SocketException((int)SocketError.HostNotFound);
    }
}
