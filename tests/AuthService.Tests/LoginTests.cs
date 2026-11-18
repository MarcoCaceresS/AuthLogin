using System.Net;
using System.Text;
using AuthService.Domain;
using AuthService.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AuthService.Tests;

public sealed class LoginTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_correcto_cumple_contrato()
    {
        var user = await TestUsers.CreateAsync(factory);

        var res = await _client.LoginAsync(user.Email, user.Password);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.Equal("bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(604800, body.GetProperty("expira_en_segundos").GetInt32());
        var f = body.GetProperty("funcionario");
        Assert.Equal(user.Id, f.GetProperty("id").GetGuid());
        Assert.Equal(user.Email, f.GetProperty("email").GetString());
        Assert.Equal("Ana Pérez", f.GetProperty("nombre").GetString());
        Assert.Equal("funcionario", f.GetProperty("rol").GetString());
        Assert.True(f.GetProperty("activo").GetBoolean());

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("access_token").GetString());
        Assert.Equal(604800, jwt.GetPayloadValue<long>("exp") - jwt.GetPayloadValue<long>("iat"));
    }

    [Fact]
    public async Task Login_guarda_sesion_y_registro()
    {
        var user = await TestUsers.CreateAsync(factory);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Api.Base}/login")
        {
            Content = JsonContent($$"""{"email":"{{user.Email}}","password":"{{user.Password}}"}"""),
        };
        req.Headers.UserAgent.ParseAdd("VigilaApp/1.0 (Android 14; SM-A546E)");

        (await _client.SendAsync(req)).EnsureSuccessStatusCode();

        await using var db = factory.NewDb();
        var session = await db.UserSessions.SingleAsync(s => s.UserId == user.Id);
        Assert.Equal(TimeSpan.FromDays(7), session.ExpiresAt - session.CreatedAt);
        Assert.Null(session.RevokedAt);
        var record = await db.LoginRecords.SingleAsync(r => r.UserId == user.Id);
        Assert.Equal(session.Id, record.SessionId);
        Assert.Equal(ApiFactory.TestIp, record.IpAddress);
        Assert.Equal("VigilaApp/1.0 (Android 14; SM-A546E)", record.UserAgent);
    }

    [Fact]
    public async Task Dos_logins_crean_dos_sesiones_y_dos_registros()
    {
        var user = await TestUsers.CreateAsync(factory);

        await _client.LoginTokenAsync(user);
        await _client.LoginTokenAsync(user);

        await using var db = factory.NewDb();
        Assert.Equal(2, await db.UserSessions.CountAsync(s => s.UserId == user.Id));
        Assert.Equal(2, await db.LoginRecords.CountAsync(r => r.UserId == user.Id));
    }

    [Fact]
    public async Task Clave_mala_y_email_inexistente_responden_igual()
    {
        var user = await TestUsers.CreateAsync(factory);

        var claveMala = await _client.LoginAsync(user.Email, "otra-clave-123");
        var noExiste = await _client.LoginAsync("nadie@victoria.cl", "otra-clave-123");

        Assert.Equal(HttpStatusCode.Unauthorized, claveMala.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noExiste.StatusCode);
        var esperado = """{"codigo":"credenciales_invalidas","mensaje":"Correo o contraseña incorrectos."}""";
        Assert.Equal(esperado, await claveMala.Content.ReadAsStringAsync());
        Assert.Equal(esperado, await noExiste.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Cuenta_inactiva_o_email_no_verificado_401(bool activo, bool verificado)
    {
        var user = await TestUsers.CreateAsync(factory, isActive: activo, isEmailVerified: verificado);

        var res = await _client.LoginAsync(user.Email, user.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("credenciales_invalidas", await res.CodigoAsync());
    }

    [Fact]
    public async Task Vecino_con_clave_correcta_403()
    {
        var vecino = await TestUsers.CreateAsync(factory, RoleNames.Usuario);

        var res = await _client.LoginAsync(vecino.Email, vecino.Password);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("sin_permiso", await res.CodigoAsync());
        await using var db = factory.NewDb();
        Assert.False(await db.UserSessions.AnyAsync(s => s.UserId == vecino.Id));
        Assert.False(await db.LoginRecords.AnyAsync(r => r.UserId == vecino.Id));
    }

    [Theory]
    [InlineData(RoleNames.Superadmin)]
    [InlineData(RoleNames.Cuadrilla)]
    public async Task Superadmin_y_cuadrilla_pueden_entrar(string rol)
    {
        var user = await TestUsers.CreateAsync(factory, rol);

        var res = await _client.LoginAsync(user.Email, user.Password);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(rol, (await res.JsonAsync()).GetProperty("funcionario").GetProperty("rol").GetString());
    }

    [Fact]
    public async Task Email_con_mayusculas_y_espacios_entra()
    {
        var user = await TestUsers.CreateAsync(factory);

        var res = await _client.LoginAsync($"  {user.Email.ToUpperInvariant()} ", user.Password);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("""{"email":"a@victoria.cl"}""")]
    [InlineData("""{"email":"","password":"clave-1234"}""")]
    [InlineData("""{"email":"a@victoria.cl","password":"   "}""")]
    [InlineData("""{"email":123}""")]
    [InlineData("no es json")]
    [InlineData("")]
    public async Task Login_con_datos_invalidos_422(string body)
    {
        var res = await _client.PostAsync($"{Api.Base}/login", JsonContent(body));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("datos_invalidos", await res.CodigoAsync());
    }

    [Fact]
    public async Task Login_fallido_no_crea_registro()
    {
        var user = await TestUsers.CreateAsync(factory);

        await _client.LoginAsync(user.Email, "otra-clave-123");

        await using var db = factory.NewDb();
        Assert.False(await db.LoginRecords.AnyAsync(r => r.UserId == user.Id));
    }

    [Fact]
    public async Task Excepcion_no_controlada_devuelve_500_error_interno()
    {
        var user = await TestUsers.CreateAsync(factory);
        var client = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IPasswordHasher<User>, ThrowingHasher>())).CreateClient();

        var res = await client.LoginAsync(user.Email, user.Password);

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Equal("""{"codigo":"error_interno","mensaje":"Ocurrió un error. Inténtalo más tarde."}""",
            await res.Content.ReadAsStringAsync());
    }

    private static StringContent JsonContent(string body) => new(body, Encoding.UTF8, "application/json");

    private sealed class ThrowingHasher : IPasswordHasher<User>
    {
        public string HashPassword(User user, string password) => throw new InvalidOperationException("boom");
        public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword) =>
            throw new InvalidOperationException("boom");
    }
}
