using System.Net;
using WebReaper.PlaygroundApi.Ssrf;
using Xunit;

namespace WebReaper.PlaygroundApi.Tests;

public class SsrfPolicyTests
{
    [Theory]
    // IPv4 loopback / unspecified / broadcast
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    // IPv4 private
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    // IPv4 link-local incl. cloud metadata
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]
    // IPv4 CGNAT
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    // IPv4 multicast / reserved
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    // IPv6 loopback / unspecified / link-local / ULA / multicast
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    // IPv4-mapped IPv6 must be judged as its IPv4 form
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void Blocks_internal_and_reserved_addresses(string ip)
    {
        Assert.True(SsrfPolicy.IsBlockedAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    // Public IPv4 (incl. addresses just outside the private/CGNAT boundaries)
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")] // just below 172.16/12
    [InlineData("172.32.0.1")]     // just above 172.16/12
    [InlineData("192.169.0.1")]    // just outside 192.168/16
    [InlineData("100.63.255.255")] // just below CGNAT
    [InlineData("100.128.0.1")]    // just above CGNAT
    [InlineData("11.0.0.1")]
    // Public IPv6
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    public void Allows_public_addresses(string ip)
    {
        Assert.False(SsrfPolicy.IsBlockedAddress(IPAddress.Parse(ip)));
    }
}
