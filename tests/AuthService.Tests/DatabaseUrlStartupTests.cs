using System.Net;
using AuthService.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Npgsql;

namespace AuthService.Tests;

/// <summary>Arranca solo con DATABASE_URL, como la entrega Render o Neon.</summary>
public sealed class DatabaseUrlFactory : ApiFactory
{
    protected override void ConfigureExtra(IWebHostBuilder builder)
    {
        var cs = new NpgsqlConnectionStringBuilder(DbConnectionString);
        var url = $"postgresql://{Uri.EscapeDataString(cs.Username!)}:{Uri.EscapeDataString(cs.Password!)}" +
                  $"@{cs.Host}:{cs.Port}/{cs.Database}?sslmode=disable";
        builder.UseSetting("ConnectionStrings:Auth", "");
        builder.UseSetting("DATABASE_URL", url);
    }
}

public sealed class DatabaseUrlStartupTests(DatabaseUrlFactory factory) : IClassFixture<DatabaseUrlFactory>
{
    [Fact]
    public async Task Con_DATABASE_URL_migra_y_permite_login()
    {
        var client = factory.CreateClient();
        var user = await TestUsers.CreateAsync(factory);

        var res = await client.LoginAsync(user.Email, user.Password);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
