using System.Net;
using AuthService.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AuthService.Tests.Infrastructure;

/// <summary>API en memoria con un PostgreSQL real y desechable por clase de test.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestSecret = "secreto-de-pruebas-solo-para-tests-0123456789";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _ = Services; // arranca el host: Program migra la base al iniciar
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Auth", _db.GetConnectionString());
        builder.UseSetting("VIGILA_JWT_SECRETO", TestSecret);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        // TestServer no trae IP remota: se fija una para poder verificar LoginRecords.
        builder.ConfigureServices(s => s.AddSingleton<IStartupFilter, FixedRemoteIp>());
        ConfigureExtra(builder);
    }

    public const string TestIp = "203.0.113.10";

    private sealed class FixedRemoteIp : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                ctx.Connection.RemoteIpAddress ??= IPAddress.Parse(TestIp);
                return nxt(ctx);
            });
            next(app);
        };
    }

    /// <summary>Punto de extensión para fábricas de tests con configuración propia.</summary>
    protected virtual void ConfigureExtra(IWebHostBuilder builder)
    {
    }

    /// <summary>Abre un DbContext propio para preparar o revisar datos.</summary>
    public AuthDbContext NewDb() =>
        Services.CreateScope().ServiceProvider.GetRequiredService<AuthDbContext>();
}
