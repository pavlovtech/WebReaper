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
    /// IPv4 tunnelled inside IPv6 is unwrapped and judged as its IPv4 form, so
    /// <c>::ffff:127.0.0.1</c> (mapped), <c>::169.254.169.254</c> (compatible),
    /// and the 6to4 / NAT64 wrappings are blocked exactly like the bare IPv4.
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

        var b = address.GetAddressBytes();

        // Unique local addresses fc00::/7 (first 7 bits 1111 110x).
        if ((b[0] & 0xFE) == 0xFC)
            return true;

        // IPv4 tunnelled inside IPv6: judge by the embedded IPv4 so an internal
        // target cannot slip past as a v6 literal (e.g. ::169.254.169.254 or the
        // 6to4 / NAT64 wrappings of it). IPv4-mapped (::ffff:0:0/96) is already
        // unwrapped in IsBlockedAddress; these are the remaining forms.
        var embedded = EmbeddedV4(b);
        return embedded is not null && IsBlockedV4(embedded);
    }

    /// <summary>
    /// The IPv4 tunnelled in an IPv6 address for the IPv4-compatible
    /// (<c>::a.b.c.d</c>), 6to4 (<c>2002::/16</c>), and NAT64 well-known prefix
    /// (<c>64:ff9b::/96</c>) forms; <c>null</c> if none. IPv4-mapped
    /// (<c>::ffff:0:0/96</c>) is handled earlier via <c>MapToIPv4</c>.
    /// </summary>
    private static byte[]? EmbeddedV4(byte[] b)
    {
        // 6to4: 2002:AABB:CCDD::/48 -> AABB.CCDD
        if (b[0] == 0x20 && b[1] == 0x02)
            return [b[2], b[3], b[4], b[5]];

        // NAT64 well-known prefix 64:ff9b::/96 -> the trailing 4 bytes.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B
            && b[4] == 0 && b[5] == 0 && b[6] == 0 && b[7] == 0
            && b[8] == 0 && b[9] == 0 && b[10] == 0 && b[11] == 0)
            return [b[12], b[13], b[14], b[15]];

        // IPv4-compatible ::a.b.c.d : the high 96 bits are zero. (:: and ::1 are
        // already handled by the IPv6Any and loopback checks upstream.)
        for (var i = 0; i < 12; i++)
            if (b[i] != 0)
                return null;
        var lowAllZero = b[12] == 0 && b[13] == 0 && b[14] == 0 && b[15] == 0;
        return lowAllZero ? null : [b[12], b[13], b[14], b[15]];
    }
}
