using System.Net;
using AuthService.Tests.Infrastructure;

namespace AuthService.Tests;

public sealed class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_responde_200()
    {
        var res = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
