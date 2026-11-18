using System.Net;
using AuthService.Domain;
using AuthService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Tests;

public sealed class AdminCreateListTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string NewEmail() => $"nuevo-{Guid.NewGuid():N}@victoria.cl";

    [Theory]
    [InlineData(RoleNames.Funcionario)]
    [InlineData(RoleNames.Cuadrilla)]
    public async Task Admin_crea_usuario_201_y_puede_entrar(string rol)
    {
        var (_, token) = await _client.AdminAsync(factory);
        var email = NewEmail();

        var res = await _client.CrearAsync(token, $"  {email.ToUpperInvariant()} ", rol);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("Juan", body.GetProperty("nombre").GetString());
        Assert.Equal("Soto", body.GetProperty("apellido").GetString());
        Assert.Equal(rol, body.GetProperty("rol").GetString());
        Assert.True(body.GetProperty("activo").GetBoolean());
        Assert.True(body.TryGetProperty("creado_en", out _));
        Assert.False(body.TryGetProperty("password_hash", out _));

        var login = await _client.LoginAsync(email, "clave-nueva-123");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Superadmin)]
    [InlineData("usuario")]
    [InlineData("jefe")]
    public async Task Crear_con_rol_no_permitido_422(string rol)
    {
        var (_, token) = await _client.AdminAsync(factory);

        var res = await _client.CrearAsync(token, NewEmail(), rol);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.Equal("datos_invalidos", body.GetProperty("codigo").GetString());
        Assert.Equal("El rol debe ser funcionario o cuadrilla.", body.GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Crear_email_repetido_409()
    {
        var (_, token) = await _client.AdminAsync(factory);
        var email = NewEmail();
        await _client.CrearAsync(token, email);

        var res = await _client.CrearAsync(token, email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("email_en_uso", await res.CodigoAsync());
    }

    [Theory]
    [InlineData("sin-arroba", "Juan", "Soto", "clave-nueva-123", "Ingresa un correo válido.")]
    [InlineData("ok@victoria.cl", " ", "Soto", "clave-nueva-123", "Ingresa nombre y apellido.")]
    [InlineData("ok@victoria.cl", "Juan", "", "clave-nueva-123", "Ingresa nombre y apellido.")]
    [InlineData("ok@victoria.cl", "Juan", "Soto", "1234567", "La contraseña debe tener entre 8 y 128 caracteres.")]
    public async Task Crear_con_datos_invalidos_422(string email, string nombre, string apellido, string password, string mensaje)
    {
        var (_, token) = await _client.AdminAsync(factory);

        var res = await _client.SendJsonAsync(HttpMethod.Post, AdminApi.Usuarios, token,
            new { email, password, nombre, apellido, rol = RoleNames.Funcionario });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal(mensaje, (await res.JsonAsync()).GetProperty("mensaje").GetString());
    }

    [Theory]
    [InlineData(RoleNames.Funcionario)]
    [InlineData(RoleNames.Cuadrilla)]
    public async Task No_superadmin_no_puede_usar_admin_403(string rol)
    {
        var user = await TestUsers.CreateAsync(factory, rol);
        var token = await _client.LoginTokenAsync(user);

        var res = await _client.SendJsonAsync(HttpMethod.Get, AdminApi.Usuarios, token);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.Equal("sin_permiso", body.GetProperty("codigo").GetString());
        Assert.Equal("No tienes permiso para realizar esta acción.", body.GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Admin_con_token_de_rol_desconocido_401()
    {
        var (_, token) = await _client.AdminAsync(factory);
        var claims = ForgedTokens.ClaimsOf(token);
        claims["rol"] = "usuario";

        var res = await _client.SendJsonAsync(HttpMethod.Get, AdminApi.Usuarios, ForgedTokens.Signed(claims, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Admin_sin_token_401()
    {
        var res = await _client.SendJsonAsync(HttpMethod.Get, AdminApi.Usuarios, null);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Admin_con_sesion_revocada_401()
    {
        var (_, token) = await _client.AdminAsync(factory);
        await _client.PostWithTokenAsync($"{Api.Base}/logout", token);

        var res = await _client.CrearAsync(token, NewEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Admin_desactivado_401()
    {
        var (admin, token) = await _client.AdminAsync(factory);
        await using (var db = factory.NewDb())
            await db.Users.Where(u => u.Id == admin.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));

        var res = await _client.CrearAsync(token, NewEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("no_autenticado", await res.CodigoAsync());
    }

    [Fact]
    public async Task Listar_filtra_por_activo_y_rol()
    {
        var (_, token) = await _client.AdminAsync(factory);
        var inactivo = await TestUsers.CreateAsync(factory, RoleNames.Cuadrilla, isActive: false);
        var activo = await TestUsers.CreateAsync(factory, RoleNames.Cuadrilla);

        var res = await _client.SendJsonAsync(HttpMethod.Get, $"{AdminApi.Usuarios}?activo=false&rol=cuadrilla", token);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = (await res.JsonAsync()).EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == inactivo.Id);
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == activo.Id);
        Assert.All(items, i =>
        {
            Assert.False(i.GetProperty("activo").GetBoolean());
            Assert.Equal("cuadrilla", i.GetProperty("rol").GetString());
            Assert.False(i.TryGetProperty("password_hash", out _));
        });
    }
}
