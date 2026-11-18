using System.Net;
using AuthService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Tests;

public sealed class LogoutTests(FakeTimeApiFactory factory) : IClassFixture<FakeTimeApiFactory>
{
    private const string Logout = $"{Api.Base}/logout";
    private const string Renovar = $"{Api.Base}/sesion/renovar";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Logout_valido_204_y_luego_renovar_401()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);

        var res = await _client.PostWithTokenAsync(Logout, token);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Empty(await res.Content.ReadAsByteArrayAsync());
        var renovar = await _client.PostWithTokenAsync(Renovar, token);
        Assert.Equal(HttpStatusCode.Unauthorized, renovar.StatusCode);
        Assert.Equal("no_autenticado", await renovar.CodigoAsync());
    }

    [Fact]
    public async Task Logout_repetido_204()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostWithTokenAsync(Logout, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostWithTokenAsync(Logout, token)).StatusCode);
    }

    [Fact]
    public async Task Logout_token_vencido_204_y_revoca()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);
        factory.Time.Advance(TimeSpan.FromDays(8));

        var res = await _client.PostWithTokenAsync(Logout, token);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        await using var db = factory.NewDb();
        Assert.NotNull((await db.UserSessions.SingleAsync(s => s.UserId == user.Id)).RevokedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("token-inventado")]
    public async Task Logout_sin_header_o_token_inventado_204(string? token)
    {
        var res = await _client.PostWithTokenAsync(Logout, token);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Logout_no_afecta_otra_sesion_del_mismo_usuario()
    {
        var user = await TestUsers.CreateAsync(factory);
        var telefono = await _client.LoginTokenAsync(user);
        var tablet = await _client.LoginTokenAsync(user);

        await _client.PostWithTokenAsync(Logout, telefono);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostWithTokenAsync(Renovar, tablet)).StatusCode);
    }
}
