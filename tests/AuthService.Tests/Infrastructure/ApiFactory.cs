using AuthService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AuthService.Tests.Infrastructure;

/// <summary>API en memoria con un PostgreSQL real y desechable por clase de test.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
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
    }

    /// <summary>Abre un DbContext propio para preparar o revisar datos.</summary>
    public AuthDbContext NewDb()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    }
}
