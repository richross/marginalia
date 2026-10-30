using System.Net;

namespace Marginalia.Web.Security;

/// <summary>
/// Resolves hostnames for validated public HTTP connections.
/// </summary>
public interface IHostAddressResolver
{
    /// <summary>
    /// Resolves every address associated with a host.
    /// </summary>
    /// <param name="host">The host to resolve.</param>
    /// <param name="cancellationToken">A token that cancels resolution.</param>
    /// <returns>All resolved addresses.</returns>
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}
