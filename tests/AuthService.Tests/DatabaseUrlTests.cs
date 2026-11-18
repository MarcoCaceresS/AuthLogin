using AuthService.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AuthService.Tests;

public sealed class DatabaseUrlTests
{
    private static NpgsqlConnectionStringBuilder Parse(string url) => new(DatabaseUrl.ToConnectionString(url));

    [Fact]
    public void Url_de_neon_con_ssl_y_channel_binding()
    {
        var cs = Parse("postgresql://ana:cl%40ve%2Fsegura@ep-cool-name-123.sa-east-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require");

        Assert.Equal("ep-cool-name-123.sa-east-1.aws.neon.tech", cs.Host);
        Assert.Equal(5432, cs.Port);
        Assert.Equal("neondb", cs.Database);
        Assert.Equal("ana", cs.Username);
        Assert.Equal("cl@ve/segura", cs.Password);
        Assert.Equal(SslMode.Require, cs.SslMode);
        Assert.Equal(ChannelBinding.Require, cs.ChannelBinding);
    }

    [Fact]
    public void Url_de_render_sin_query_y_con_puerto()
    {
        var cs = Parse("postgres://auth_user:secreto@dpg-abc123-a:6543/auth_db");

        Assert.Equal("dpg-abc123-a", cs.Host);
        Assert.Equal(6543, cs.Port);
        Assert.Equal("auth_db", cs.Database);
        Assert.Equal("auth_user", cs.Username);
        Assert.Equal("secreto", cs.Password);
    }

    [Theory]
    [InlineData("disable", SslMode.Disable)]
    [InlineData("prefer", SslMode.Prefer)]
    [InlineData("verify-ca", SslMode.VerifyCA)]
    [InlineData("verify-full", SslMode.VerifyFull)]
    public void Traduce_sslmode(string valor, SslMode esperado) =>
        Assert.Equal(esperado, Parse($"postgresql://u:p@h/db?sslmode={valor}").SslMode);

    [Fact]
    public void Ignora_parametros_desconocidos() =>
        Assert.Equal("db", Parse("postgresql://u:p@h/db?application_name=x&foo=bar").Database);

    [Theory]
    [InlineData("mysql://u:p@h/db")]
    [InlineData("no es una url")]
    [InlineData("postgresql://u:p@h/")]
    public void Url_invalida_falla_sin_mostrar_la_clave(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseUrl.ToConnectionString(url));

        Assert.DoesNotContain("p@h", ex.Message);
        Assert.Contains("DATABASE_URL", ex.Message);
    }

    [Fact]
    public void ConnectionStrings_Auth_tiene_prioridad()
    {
        var config = Config(("ConnectionStrings:Auth", "Host=uno;Database=a"), ("DATABASE_URL", "postgresql://u:p@dos/b"));

        Assert.Equal("Host=uno;Database=a", DatabaseUrl.Resolve(config));
    }

    [Fact]
    public void Sin_ConnectionStrings_Auth_usa_DATABASE_URL()
    {
        var config = Config(("ConnectionStrings:Auth", ""), ("DATABASE_URL", "postgresql://u:p@dos/b"));

        Assert.Equal("dos", new NpgsqlConnectionStringBuilder(DatabaseUrl.Resolve(config)).Host);
    }

    [Fact]
    public void Sin_ninguna_falla_con_mensaje_claro()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseUrl.Resolve(Config()));

        Assert.Contains("ConnectionStrings__Auth", ex.Message);
        Assert.Contains("DATABASE_URL", ex.Message);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}
