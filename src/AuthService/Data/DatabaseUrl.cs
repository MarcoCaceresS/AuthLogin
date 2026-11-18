using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace AuthService.Data;

/// <summary>
/// Render y Neon entregan la conexión como URL (postgresql://user:clave@host:puerto/base?sslmode=require).
/// Npgsql solo entiende el formato clave=valor, así que aquí se traduce.
/// </summary>
public static class DatabaseUrl
{
    /// <summary>ConnectionStrings:Auth si existe; si no, DATABASE_URL traducida.</summary>
    public static string Resolve(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Auth");
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        var url = config["DATABASE_URL"];
        if (!string.IsNullOrWhiteSpace(url))
            return ToConnectionString(url);

        throw new InvalidOperationException(
            "Falta la conexión a la base: define ConnectionStrings__Auth o DATABASE_URL.");
    }

    public static string ToConnectionString(string url)
    {
        // Los mensajes nunca incluyen la URL: lleva la clave.
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql"))
            throw Invalid("debe empezar con postgres:// o postgresql://");

        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (database.Length == 0)
            throw Invalid("falta el nombre de la base después del host");

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = database,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        foreach (var (key, value) in QueryHelpers.ParseQuery(uri.Query))
        {
            switch (key)
            {
                case "sslmode":
                    builder.SslMode = value.ToString() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "require" => SslMode.Require,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => throw Invalid("sslmode no reconocido"),
                    };
                    break;
                case "channel_binding":
                    builder.ChannelBinding = value.ToString() switch
                    {
                        "disable" => ChannelBinding.Disable,
                        "prefer" => ChannelBinding.Prefer,
                        "require" => ChannelBinding.Require,
                        _ => throw Invalid("channel_binding no reconocido"),
                    };
                    break;
                // Otros parámetros (application_name, options…) no se usan aquí.
            }
        }

        return builder.ConnectionString;
    }

    private static InvalidOperationException Invalid(string problema) => new($"DATABASE_URL no es válida: {problema}.");
}
