using System.Net;
using AuthService.Domain;
using AuthService.Tests.Infrastructure;

namespace AuthService.Tests;

public sealed class AdminManageTests(FakeTimeApiFactory factory) : IClassFixture<FakeTimeApiFactory>
{
    private const string Renovar = $"{Api.Base}/sesion/renovar";
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> EstadoAsync(string token, Guid id, bool? activo) =>
        _client.SendJsonAsync(HttpMethod.Patch, $"{AdminApi.Usuarios}/{id}/estado", token, new { activo });

    private Task<HttpResponseMessage> ClaveAsync(string token, Guid id, string password) =>
        _client.SendJsonAsync(HttpMethod.Post, $"{AdminApi.Usuarios}/{id}/clave", token, new { password });

    [Fact]
    public async Task Desactivar_revoca_sesiones()
    {
        var (_, admin) = await _client.AdminAsync(factory);
        var user = await TestUsers.CreateAsync(factory);
        var telefono = await _client.LoginTokenAsync(user);
        var tablet = await _client.LoginTokenAsync(user);

        var res = await EstadoAsync(admin, user.Id, false);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False((await res.JsonAsync()).GetProperty("activo").GetBoolean());
        foreach (var token in new[] { telefono, tablet })
        {
            var renovar = await _client.PostWithTokenAsync(Renovar, token);
            Assert.Equal(HttpStatusCode.Unauthorized, renovar.StatusCode);
            Assert.Equal("cuenta_inactiva", await renovar.CodigoAsync());
        }
        var login = await _client.LoginAsync(user.Email, user.Password);
        Assert.Equal("credenciales_invalidas", await login.CodigoAsync());
    }

    [Fact]
    public async Task Reactivar_permite_login_nuevo()
    {
        var (_, admin) = await _client.AdminAsync(factory);
        var user = await TestUsers.CreateAsync(factory);
        var viejo = await _client.LoginTokenAsync(user);
        await EstadoAsync(admin, user.Id, false);

        var res = await EstadoAsync(admin, user.Id, true);

        Assert.True((await res.JsonAsync()).GetProperty("activo").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await _client.LoginAsync(user.Email, user.Password)).StatusCode);
        Assert.Equal("no_autenticado", await (await _client.PostWithTokenAsync(Renovar, viejo)).CodigoAsync());
    }

    [Fact]
    public async Task Desactivarse_a_si_mismo_403()
    {
        var (me, admin) = await _client.AdminAsync(factory);

        var res = await EstadoAsync(admin, me.Id, false);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("sin_permiso", await res.CodigoAsync());
    }

    [Fact]
    public async Task Desactivar_otro_superadmin_403()
    {
        var (_, admin) = await _client.AdminAsync(factory);
        var otro = await TestUsers.CreateAsync(factory, RoleNames.Superadmin);

        var res = await EstadoAsync(admin, otro.Id, false);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("No tienes permiso para realizar esta acción.", (await res.JsonAsync()).GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Estado_sin_campo_activo_422()
    {
        var (_, admin) = await _client.AdminAsync(factory);
        var user = await TestUsers.CreateAsync(factory);

        var res = await EstadoAsync(admin, user.Id, null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("datos_invalidos", await res.CodigoAsync());
    }

    [Fact]
    public async Task Id_inexistente_404()
    {
        var (_, admin) = await _client.AdminAsync(factory);

        var estado = await EstadoAsync(admin, Guid.NewGuid(), false);
        var clave = await ClaveAsync(admin, Guid.NewGuid(), "clave-nueva-999");
        var ingresos = await _client.SendJsonAsync(HttpMethod.Get, $"{AdminApi.Usuarios}/{Guid.NewGuid()}/ingresos", admin);

        foreach (var res in new[] { estado, clave, ingresos })
        {
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Equal("usuario_no_encontrado", await res.CodigoAsync());
        }
    }

    [Fact]
    public async Task Reset_clave_204_revoca_sesiones()
    {
        var (_, admin) = await _client.AdminAsync(factory);
        var user = await TestUsers.CreateAsync(factory);
        var viejo = await _client.LoginTokenAsync(user);

        var res = await ClaveAsync(admin, user.Id, "clave-nueva-999");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal("no_autenticado", await (await _client.PostWithTokenAsync(Renovar, viejo)).CodigoAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.LoginAsync(user.Email, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.LoginAsync(user.Email, "clave-nueva-999")).StatusCode);
    }

    [Fact]
    public async Task Reset_clave_corta_422()
    {
        var (_, admin) = await _client.AdminAsync(factory);
        var user = await TestUsers.CreateAsync(factory);

        var res = await ClaveAsync(admin, user.Id, "1234567");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("La contraseña debe tener entre 8 y 128 caracteres.", (await res.JsonAsync()).GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Reset_clave_de_superadmin_403()
    {
        var (me, admin) = await _client.AdminAsync(factory);
        var otro = await TestUsers.CreateAsync(factory, RoleNames.Superadmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await ClaveAsync(admin, otro.Id, "clave-nueva-999")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClaveAsync(admin, me.Id, "clave-nueva-999")).StatusCode);
    }

    [Fact]
    public async Task Ingresos_en_orden_descendente()
    {
        var (_, admin) = await _client.AdminAsync(factory);
        var user = await TestUsers.CreateAsync(factory);
        for (var i = 0; i < 3; i++)
        {
            await _client.LoginTokenAsync(user);
            factory.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var res = await _client.SendJsonAsync(HttpMethod.Get, $"{AdminApi.Usuarios}/{user.Id}/ingresos", admin);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = (await res.JsonAsync()).EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        var fechas = items.Select(i => i.GetProperty("fecha").GetDateTimeOffset()).ToList();
        Assert.Equal(fechas.OrderByDescending(f => f), fechas);
        Assert.Equal(factory.Time.Now - TimeSpan.FromMinutes(1), fechas[0]);
        Assert.Equal(ApiFactory.TestIp, items[0].GetProperty("ip").GetString());
        Assert.True(items[0].TryGetProperty("dispositivo", out _));
    }
}
