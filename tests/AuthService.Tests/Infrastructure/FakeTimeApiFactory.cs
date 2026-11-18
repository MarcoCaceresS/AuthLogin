using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Tests.Infrastructure;

/// <summary>ApiFactory con reloj controlado por el test (emisión y validación de tokens).</summary>
public sealed class FakeTimeApiFactory : ApiFactory
{
    public FakeTime Time { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    protected override void ConfigureExtra(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Time));
}
