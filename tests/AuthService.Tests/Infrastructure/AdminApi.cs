using System.Net.Http.Json;
using AuthService.Domain;

namespace AuthService.Tests.Infrastructure;

public static class AdminApi
{
    public const string Usuarios = "/api/v1/admin/usuarios";

    /// <summary>Crea un superadmin y devuelve su token.</summary>
    public static async Task<(TestUser Admin, string Token)> AdminAsync(this HttpClient c, ApiFactory factory)
    {
        var admin = await TestUsers.CreateAsync(factory, RoleNames.Superadmin);
        return (admin, await c.LoginTokenAsync(admin));
    }

    public static Task<HttpResponseMessage> SendJsonAsync(
        this HttpClient c, HttpMethod method, string url, string? token, object? body = null)
    {
        var req = Api.WithToken(method, url, token);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return c.SendAsync(req);
    }

    public static Task<HttpResponseMessage> CrearAsync(this HttpClient c, string token, string email,
        string rol = RoleNames.Funcionario, string password = "clave-nueva-123") =>
        c.SendJsonAsync(HttpMethod.Post, Usuarios, token,
            new { email, password, nombre = "Juan", apellido = "Soto", rol });
}
