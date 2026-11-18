using System.Net;
using AuthService.Features.Cuadrilla;

namespace AuthService.Tests;

public sealed class ClientIpTests
{
    [Theory]
    [InlineData("::ffff:172.18.0.1", "172.18.0.1")]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    public void Ip_mapeada_se_guarda_como_ipv4(string remote, string esperado) =>
        Assert.Equal(esperado, CuadrillaEndpoints.ClientIp(IPAddress.Parse(remote)));

    [Fact]
    public void Sin_ip_devuelve_null() => Assert.Null(CuadrillaEndpoints.ClientIp(null));
}
