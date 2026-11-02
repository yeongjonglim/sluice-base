using System.Net;
using System.Net.Sockets;

namespace SluiceBase.Api.Services;

internal static class PublicAddress
{
    // IANA IPv4 special-purpose ranges that are not globally reachable.
    private static readonly IPNetwork[] NonPublicV4 =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"), // CGNAT
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"), // IETF protocol assignments
        IPNetwork.Parse("192.0.2.0/24"), // TEST-NET-1
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"), // benchmarking
        IPNetwork.Parse("198.51.100.0/24"), // TEST-NET-2
        IPNetwork.Parse("203.0.113.0/24"), // TEST-NET-3
        IPNetwork.Parse("224.0.0.0/3"), // multicast, reserved, broadcast
    ];

    // Allowlist global unicast so loopback, unspecified, link-local, unique-local, multicast and the
    // IPv4-compatible/NAT64 embeddings all fall out without enumerating them.
    private static readonly IPNetwork GlobalUnicastV6 = IPNetwork.Parse("2000::/3");

    private static readonly IPNetwork[] NonPublicV6 =
    [
        IPNetwork.Parse("2001::/32"), // Teredo tunnels to an arbitrary embedded IPv4 address
        IPNetwork.Parse("2001:db8::/32"), // documentation
    ];

    private static readonly IPNetwork SixToFour = IPNetwork.Parse("2002::/16");

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !NonPublicV4.Any(n => n.Contains(address)),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            _ => false,
        };
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (!GlobalUnicastV6.Contains(address) || NonPublicV6.Any(n => n.Contains(address)))
        {
            return false;
        }

        // 6to4 embeds the IPv4 relay target in bytes 2..5.
        return !SixToFour.Contains(address) || IsPublic(new IPAddress(address.GetAddressBytes().AsSpan(2, 4)));
    }
}
