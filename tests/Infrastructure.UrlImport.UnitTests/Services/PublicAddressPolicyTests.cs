using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class PublicAddressPolicyTests
{
    [Theory]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.0.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("192.88.99.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("2001::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002::1", false)]
    [InlineData("3fff::1", false)]
    [InlineData("2606:4700:4700::1111%1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("::ffff:1.1.1.1", true)]
    public void IsPublic_WhenAddressIsClassified_ReturnsExpectedResult(
        string value,
        bool expected)
    {
        // Arrange
        var address = IPAddress.Parse(value);

        // Act
        var actual = PublicAddressPolicy.IsPublic(address);

        // Assert
        Assert.Equal(
            expected,
            actual);
    }
}
