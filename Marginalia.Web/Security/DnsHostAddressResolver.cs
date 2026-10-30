using System.Net;

namespace Marginalia.Web.Security;

/// <summary>
/// Resolves hostnames using the operating system resolver.
/// </summary>
public sealed class DnsHostAddressResolver : IHostAddressResolver
{
    /// <inheritdoc />
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return IPAddress.TryParse(host, out var address)
            ? Task.FromResult<IPAddress[]>([address])
            : Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}
