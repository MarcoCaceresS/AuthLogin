using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AuthService.Tests.Infrastructure;

/// <summary>Atajos HTTP para los tests.</summary>
public static class Api
{
    public const string Base = "/api/v1/cuadrilla";

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient c, string email, string password) =>
        c.PostAsJsonAsync($"{Base}/login", new { email, password });

    public static async Task<string> LoginTokenAsync(this HttpClient c, TestUser u)
    {
        var res = await c.LoginAsync(u.Email, u.Password);
        res.EnsureSuccessStatusCode();
        return (await res.JsonAsync()).GetProperty("access_token").GetString()!;
    }

    public static Task<HttpResponseMessage> PostWithTokenAsync(this HttpClient c, string url, string? token) =>
        c.SendAsync(WithToken(HttpMethod.Post, url, token));

    public static HttpRequestMessage WithToken(HttpMethod method, string url, string? token)
    {
        var req = new HttpRequestMessage(method, url);
        if (token is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    public static async Task<string?> CodigoAsync(this HttpResponseMessage res) =>
        (await res.JsonAsync()).GetProperty("codigo").GetString();
}
