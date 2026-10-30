using System.Net;
using System.Net.Sockets;

namespace Marginalia.Web.Security;

/// <summary>
/// Rejects IP addresses that are not suitable public destinations for remote metadata fetches.
/// </summary>
public static class PublicAddressPolicy
{
    private static readonly (IPAddress Network, int PrefixLength)[] BlockedIpv4Networks =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.0.2.0"), 24),
        (IPAddress.Parse("192.88.99.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("198.51.100.0"), 24),
        (IPAddress.Parse("203.0.113.0"), 24),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4)
    ];

    private static readonly (IPAddress Network, int PrefixLength)[] BlockedIpv6Networks =
    [
        (IPAddress.Parse("2001::"), 23),
        (IPAddress.Parse("2001:db8::"), 32),
        (IPAddress.Parse("2002::"), 16),
        (IPAddress.Parse("3fff::"), 20)
    ];

    /// <summary>
    /// Determines whether an address is globally routable and not within a reserved range.
    /// </summary>
    /// <param name="address">The address to inspect.</param>
    /// <returns><see langword="true"/> when the address is an approved public destination.</returns>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return !BlockedIpv4Networks.Any(network => IsInNetwork(address, network.Network, network.PrefixLength))
                && !IPAddress.IsLoopback(address);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if ((bytes[0] & 0xE0) != 0x20)
        {
            return false;
        }

        return !BlockedIpv6Networks.Any(network => IsInNetwork(address, network.Network, network.PrefixLength));
    }

    private static bool IsInNetwork(IPAddress address, IPAddress network, int prefixLength)
    {
        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        if (addressBytes.Length != networkBytes.Length)
        {
            return false;
        }

        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        for (var index = 0; index < wholeBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[wholeBytes] & mask) == (networkBytes[wholeBytes] & mask);
    }
}
