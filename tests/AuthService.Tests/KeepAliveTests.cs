using System.Collections.Concurrent;
using AuthService.Hosting;
using AuthService.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Tests;

/// <summary>Captura las llamadas del keep-alive en vez de salir a la red.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    public ConcurrentQueue<Uri> Calls { get; } = new();
    public bool Fail { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls.Enqueue(request.RequestUri!);
        if (Fail)
            throw new HttpRequestException("sin red");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}

public class KeepAliveFactory : ApiFactory
{
    public RecordingHandler Handler { get; } = new();
    protected virtual string? Url => "https://vigila-auth.onrender.com/";

    protected override void ConfigureExtra(IWebHostBuilder builder)
    {
        if (Url is not null)
            builder.UseSetting("RENDER_EXTERNAL_URL", Url);
        builder.UseSetting("KeepAlive:Interval", "00:00:00.200");
        builder.ConfigureTestServices(s =>
            s.AddHttpClient(KeepAliveService.ClientName).ConfigurePrimaryHttpMessageHandler(() => Handler));
    }
}

public sealed class KeepAliveDisabledFactory : KeepAliveFactory
{
    protected override string? Url => null;
}

public sealed class KeepAliveTests(KeepAliveFactory factory) : IClassFixture<KeepAliveFactory>
{
    [Fact]
    public async Task Llama_a_health_de_la_url_publica_una_y_otra_vez()
    {
        await WaitForCallsAsync(factory.Handler, 2);

        Assert.All(factory.Handler.Calls, u => Assert.Equal("https://vigila-auth.onrender.com/health", u.ToString()));
    }

    [Fact]
    public async Task Sigue_intentando_si_una_llamada_falla()
    {
        factory.Handler.Fail = true;
        var before = factory.Handler.Calls.Count;

        await WaitForCallsAsync(factory.Handler, before + 2);
        factory.Handler.Fail = false;
    }

    private static async Task WaitForCallsAsync(RecordingHandler handler, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (handler.Calls.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(handler.Calls.Count >= count, $"Esperaba {count} llamadas, hubo {handler.Calls.Count}.");
    }
}

public sealed class KeepAliveDisabledTests(KeepAliveDisabledFactory factory) : IClassFixture<KeepAliveDisabledFactory>
{
    [Fact]
    public async Task Sin_url_no_hace_llamadas()
    {
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Empty(factory.Handler.Calls);
    }
}
