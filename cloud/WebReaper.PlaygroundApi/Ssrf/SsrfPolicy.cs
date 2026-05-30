using System.Net;
using System.Net.Sockets;

namespace WebReaper.PlaygroundApi.Ssrf;

/// <summary>
/// The SSRF address policy: classifies a resolved IP as safe-to-reach or not.
/// This is the pure, security-critical core of the egress guard (Phase 1 of
/// docs/CLOUD-PLAYGROUND-PHASE-1.md), kept free of I/O so it is exhaustively
/// unit-testable. A public any-URL fetcher MUST reject loopback, private,
/// link-local (incl. the cloud-metadata address), CGNAT, and the IPv6
/// equivalents, or it is an SSRF pivot.
/// </summary>
public static class SsrfPolicy
{
    /// <summary>
    /// True if a connection to <paramref name="address"/> must be refused.
    /// IPv4-mapped IPv6 addresses are unwrapped and judged as their IPv4 form,
    /// so <c>::ffff:127.0.0.1</c> is blocked exactly like <c>127.0.0.1</c>.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        // Loopback (127.0.0.0/8, ::1) for either family.
        if (IPAddress.IsLoopback(address))
            return true;

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsBlockedV6(address),
            // Anything that is not plain IPv4/IPv6 (Unix sockets, etc.) is not
            // a legitimate scrape target.
            _ => true,
        };
    }

    private static bool IsBlockedV4(byte[] b)
    {
        // b is 4 bytes.
        return b[0] switch
        {
            0 => true,                                   // 0.0.0.0/8  "this network" / unspecified
            10 => true,                                  // 10.0.0.0/8 private
            127 => true,                                 // loopback (also caught above)
            100 when b[1] is >= 64 and <= 127 => true,   // 100.64.0.0/10 CGNAT
            169 when b[1] == 254 => true,                // 169.254.0.0/16 link-local incl. 169.254.169.254 metadata
            172 when b[1] is >= 16 and <= 31 => true,    // 172.16.0.0/12 private
            192 when b[1] == 168 => true,                // 192.168.0.0/16 private
            >= 224 => true,                              // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved + 255.255.255.255 broadcast
            _ => false,
        };
    }

    private static bool IsBlockedV6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6Any))           // ::  unspecified
            return true;
        if (address.IsIPv6LinkLocal)                     // fe80::/10
            return true;
        if (address.IsIPv6SiteLocal)                     // fec0::/10 (deprecated, still refuse)
            return true;
        if (address.IsIPv6Multicast)                     // ff00::/8
            return true;

        // Unique local addresses fc00::/7 (first 7 bits 1111 110x).
        var b = address.GetAddressBytes();
        return (b[0] & 0xFE) == 0xFC;
    }
}
