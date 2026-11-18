using System.Net;
using AuthService.Domain;
using AuthService.Security;
using AuthService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace AuthService.Tests;

public sealed class RenewTests(FakeTimeApiFactory factory) : IClassFixture<FakeTimeApiFactory>
{
    private const string Renovar = $"{Api.Base}/sesion/renovar";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Renovar_dia_3_extiende_7_dias()
    {
        var user = await TestUsers.CreateAsync(factory);
        var original = await _client.LoginTokenAsync(user);
        factory.Time.Advance(TimeSpan.FromDays(3));
        var dia3 = factory.Time.Now;

        var res = await _client.PostWithTokenAsync(Renovar, original);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.Equal(604800, body.GetProperty("expira_en_segundos").GetInt32());
        Assert.Equal(user.Id, body.GetProperty("funcionario").GetProperty("id").GetGuid());
        var nuevo = body.GetProperty("access_token").GetString()!;
        var handler = new JsonWebTokenHandler();
        var jwtNuevo = handler.ReadJsonWebToken(nuevo);
        Assert.NotEqual(handler.ReadJsonWebToken(original).Id, jwtNuevo.Id);
        Assert.Equal((dia3 + TimeSpan.FromDays(7)).ToUnixTimeSeconds(), jwtNuevo.GetPayloadValue<long>("exp"));

        await using var db = factory.NewDb();
        var session = await db.UserSessions.SingleAsync(s => s.UserId == user.Id);
        Assert.Equal(dia3 + TimeSpan.FromDays(7), session.ExpiresAt);
        Assert.Equal(TokenIssuer.HashJti(jwtNuevo.Id), session.TokenHash);

        // El token renovado también renueva.
        Assert.Equal(HttpStatusCode.OK, (await _client.PostWithTokenAsync(Renovar, nuevo)).StatusCode);
    }

    [Fact]
    public async Task Renovar_vencido_por_1_segundo_401()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);
        factory.Time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));

        var res = await _client.PostWithTokenAsync(Renovar, token);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("sesion_expirada", await res.CodigoAsync());
    }

    [Fact]
    public async Task Renovar_en_el_segundo_exacto_del_vencimiento_401()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);
        factory.Time.Advance(TimeSpan.FromDays(7));

        var res = await _client.PostWithTokenAsync(Renovar, token);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("sesion_expirada", await res.CodigoAsync());
    }

    [Fact]
    public async Task Renovar_un_segundo_antes_del_vencimiento_200()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);
        factory.Time.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.OK, (await _client.PostWithTokenAsync(Renovar, token)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("token-inventado")]
    public async Task Renovar_sin_header_o_token_inventado_401(string? token)
    {
        var res = await _client.PostWithTokenAsync(Renovar, token);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Renovar_token_con_otra_firma_401()
    {
        // sub y sid de una sesión real y activa: solo la firma puede rechazarlo.
        var user = await TestUsers.CreateAsync(factory);
        var claims = ForgedTokens.ClaimsOf(await _client.LoginTokenAsync(user));
        var token = ForgedTokens.Signed(claims, factory.Time.Now, secret: "otro-secreto-distinto-de-32-bytes-o-mas!!");

        var res = await _client.PostWithTokenAsync(Renovar, token);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Renovar_token_sin_firma_alg_none_401()
    {
        var user = await TestUsers.CreateAsync(factory);
        var claims = ForgedTokens.ClaimsOf(await _client.LoginTokenAsync(user));

        var res = await _client.PostWithTokenAsync(Renovar, ForgedTokens.Unsigned(claims, factory.Time.Now));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Renovar_cuenta_desactivada_401()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);
        await using (var db = factory.NewDb())
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));

        var res = await _client.PostWithTokenAsync(Renovar, token);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("cuenta_inactiva", await res.CodigoAsync());
        Assert.Equal("Tu cuenta fue desactivada.", (await res.JsonAsync()).GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Renovar_dos_veces_con_mismo_token_funciona()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostWithTokenAsync(Renovar, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostWithTokenAsync(Renovar, token)).StatusCode);
    }

    [Fact]
    public async Task Renovar_token_sin_sid_401()
    {
        // El usuario tiene una sesión activa: solo la falta de sid puede rechazarlo.
        var user = await TestUsers.CreateAsync(factory);
        var claims = ForgedTokens.ClaimsOf(await _client.LoginTokenAsync(user), keepSid: false);

        var res = await _client.PostWithTokenAsync(Renovar, ForgedTokens.Signed(claims, factory.Time.Now));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Renovar_con_token_de_rol_desconocido_401()
    {
        // Por ejemplo un token de vecino ("usuario") firmado por otro sistema con el mismo secreto.
        var user = await TestUsers.CreateAsync(factory);
        var claims = ForgedTokens.ClaimsOf(await _client.LoginTokenAsync(user));
        claims["rol"] = "usuario";

        var res = await _client.PostWithTokenAsync(Renovar, ForgedTokens.Signed(claims, factory.Time.Now));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Renovar_no_crea_login_record()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);

        (await _client.PostWithTokenAsync(Renovar, token)).EnsureSuccessStatusCode();

        await using var db = factory.NewDb();
        Assert.Equal(1, await db.LoginRecords.CountAsync(r => r.UserId == user.Id));
    }
}
