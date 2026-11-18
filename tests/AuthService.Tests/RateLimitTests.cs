using System.Net;
using AuthService.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace AuthService.Tests;

/// <summary>Límites reales del contrato para login (10/min por IP); renovar se baja a 3 para probarlo rápido.</summary>
public sealed class RateLimitFactory : ApiFactory
{
    protected override void ConfigureExtra(IWebHostBuilder builder)
    {
        builder.UseSetting("RateLimits:LoginPerMinute", "10");
        builder.UseSetting("RateLimits:RenewPerMinute", "3");
    }
}

public sealed class RateLimitTests(RateLimitFactory factory) : IClassFixture<RateLimitFactory>
{
    private const string Renovar = $"{Api.Base}/sesion/renovar";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Once_logins_bloquean_el_siguiente_pero_no_renovar()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user); // intento 1
        for (var i = 2; i <= 10; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await _client.LoginAsync(user.Email, "otra-clave-123")).StatusCode);

        var bloqueado = await _client.LoginAsync(user.Email, user.Password); // intento 11

        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueado.StatusCode);
        var body = await bloqueado.JsonAsync();
        Assert.Equal("demasiados_intentos", body.GetProperty("codigo").GetString());
        Assert.Equal("Demasiados intentos. Espera un momento e inténtalo de nuevo.", body.GetProperty("mensaje").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _client.PostWithTokenAsync(Renovar, token)).StatusCode);
    }
}

public sealed class RenewRateLimitTests(RateLimitFactory factory) : IClassFixture<RateLimitFactory>
{
    private const string Renovar = $"{Api.Base}/sesion/renovar";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Renovar_tiene_limite_por_funcionario()
    {
        var ana = await TestUsers.CreateAsync(factory);
        var luis = await TestUsers.CreateAsync(factory);
        var tokenAna = await _client.LoginTokenAsync(ana);
        var tokenLuis = await _client.LoginTokenAsync(luis);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await _client.PostWithTokenAsync(Renovar, tokenAna)).StatusCode);

        var bloqueado = await _client.PostWithTokenAsync(Renovar, tokenAna);

        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueado.StatusCode);
        Assert.Equal("demasiados_intentos", await bloqueado.CodigoAsync());
        Assert.Equal(HttpStatusCode.OK, (await _client.PostWithTokenAsync(Renovar, tokenLuis)).StatusCode);
    }
}
