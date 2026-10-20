using System.Net;
using SluiceBase.Api.Mcp.Clients;

namespace SluiceBase.Api.Tests;

public class PublicAddressTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12::1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("ff02::1")]
    public void IsPublic_False(string address)
    {
        Assert.False(PublicAddress.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("160.79.104.10")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublic_True(string address)
    {
        Assert.True(PublicAddress.IsPublic(IPAddress.Parse(address)));
    }
}
