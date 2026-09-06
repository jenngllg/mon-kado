using System.Net;
using System.Net.Sockets;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Rejects non-public and special-purpose network destinations.</summary>
public static class PublicAddressPolicy
{
    private static readonly IPNetwork[] _excludedNetworks = [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/3"),
        IPNetwork.Parse("2001::/23"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("3fff::/20")
    ];
    private static readonly IPNetwork _globalIpv6 = IPNetwork.Parse("2000::/3");
    /// <summary>Checks a resolved address, including mapped IPv4 representations.</summary>
    /// <param name="address">The resolved connection address.</param>
    /// <returns>Whether the address is an ordinary global unicast destination.</returns>
    public static bool IsPublic(IPAddress address)
    {

        if (address.IsIPv4MappedToIPv6)
            return IsPublic(address.MapToIPv4());

        if (address.AddressFamily == AddressFamily.InterNetworkV6 && (address.ScopeId != 0 || !_globalIpv6.Contains(address)))
            return false;

        return !_excludedNetworks.Any(network => network.Contains(address));
    }
}
