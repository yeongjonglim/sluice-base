using System.Net;
using System.Net.Sockets;

namespace SluiceBase.Api.Mcp.Clients;

internal static class PublicAddress
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(address.GetAddressBytes()),
            _ => false,
        };
    }

    private static bool IsPublicV4(ReadOnlySpan<byte> b)
    {
        return b[0] switch
        {
            0 or 10 or 127 => false,
            100 when (b[1] & 0xC0) == 64 => false, // 100.64.0.0/10 CGNAT
            169 when b[1] == 254 => false,
            172 when (b[1] & 0xF0) == 16 => false,
            192 when b[1] == 168 => false,
            192 when b[1] == 0 && b[2] is 0 or 2 => false, // IETF protocol assignments, TEST-NET-1
            198 when (b[1] & 0xFE) == 18 => false, // 198.18.0.0/15 benchmarking
            198 when b[1] == 51 && b[2] == 100 => false,
            203 when b[1] == 0 && b[2] == 113 => false,
            >= 224 => false, // multicast, reserved, broadcast
            _ => true,
        };
    }

    // Allowlist global unicast (2000::/3) so loopback, unspecified, link-local, unique-local,
    // multicast and the IPv4-compatible/NAT64 embeddings all fall out without enumerating them.
    private static bool IsPublicV6(ReadOnlySpan<byte> b)
    {
        if ((b[0] & 0xE0) != 0x20)
        {
            return false;
        }

        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
        {
            return false; // Teredo tunnels to an arbitrary embedded IPv4 address
        }

        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
        {
            return false; // documentation
        }

        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicV4(b.Slice(2, 4)); // 6to4 embeds the IPv4 relay target
        }

        return true;
    }
}
