namespace AuthService.Hosting;

/// <summary>
/// Render (plan gratuito) duerme el servicio tras 15 minutos sin tráfico de entrada.
/// Este servicio se llama a sí mismo por su URL pública cada cierto tiempo (10 min por defecto):
/// la llamada pasa por el proxy de Render y cuenta como tráfico, así no se duerme.
/// Se activa solo si hay URL: KeepAlive:Url o RENDER_EXTERNAL_URL (Render la define solo).
/// </summary>
public sealed class KeepAliveService(
    IHttpClientFactory httpClients,
    IConfiguration config,
    ILogger<KeepAliveService> logger) : BackgroundService
{
    public const string ClientName = "keepalive";
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(10);

    public static Uri? HealthUrl(IConfiguration config)
    {
        var baseUrl = config["KeepAlive:Url"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = config["RENDER_EXTERNAL_URL"];
        return string.IsNullOrWhiteSpace(baseUrl) ? null : new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "health");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = HealthUrl(config);
        if (url is null)
            return;

        var interval = config.GetValue("KeepAlive:Interval", DefaultInterval);
        logger.LogInformation("Keep-alive activo: GET {Url} cada {Interval}.", url, interval);

        var client = httpClients.CreateClient(ClientName);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var res = await client.GetAsync(url, stoppingToken);
                if (!res.IsSuccessStatusCode)
                    logger.LogWarning("Keep-alive: {Url} respondió {Status}.", url, (int)res.StatusCode);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                // Una falla suelta no detiene el ciclo: se reintenta en el próximo intervalo.
                logger.LogWarning("Keep-alive: no se pudo llamar a {Url}: {Error}", url, e.Message);
            }
        }
    }
}
