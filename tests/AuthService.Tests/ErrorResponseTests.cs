using System.Net;
using System.Text;
using AuthService.Tests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AuthService.Tests;

public sealed class ErrorResponseTests(LogCaptureApiFactory factory) : IClassFixture<LogCaptureApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Admin_body_invalido_usa_mensaje_generico()
    {
        var (_, token) = await _client.AdminAsync(factory);
        var req = Api.WithToken(HttpMethod.Post, AdminApi.Usuarios, token);
        req.Content = new StringContent("no es json", Encoding.UTF8, "application/json");

        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("""{"codigo":"datos_invalidos","mensaje":"Revisa los datos enviados."}""", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_query_invalida_usa_mensaje_generico()
    {
        var (_, token) = await _client.AdminAsync(factory);

        var res = await _client.SendJsonAsync(HttpMethod.Get, $"{AdminApi.Usuarios}?activo=abc", token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("Revisa los datos enviados.", (await res.JsonAsync()).GetProperty("mensaje").GetString());
    }

    [Theory]
    [InlineData("GET", "/api/v1/no-existe")]
    [InlineData("PATCH", "/api/v1/admin/usuarios/no-es-guid/estado")]
    public async Task Ruta_inexistente_404_con_cuerpo(string method, string url)
    {
        var res = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("""{"codigo":"no_encontrado","mensaje":"La ruta no existe."}""", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Metodo_equivocado_405_con_cuerpo()
    {
        var res = await _client.GetAsync($"{Api.Base}/login");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, res.StatusCode);
        Assert.Equal("""{"codigo":"metodo_no_permitido","mensaje":"Método no permitido en esta ruta."}""", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Body_invalido_no_se_registra_como_error()
    {
        var before = factory.Logs.Count;

        await _client.PostAsync($"{Api.Base}/login", new StringContent("no es json", Encoding.UTF8, "application/json"));

        Assert.DoesNotContain(factory.Logs.Skip(before), l => l.Level >= LogLevel.Error);
    }
}
