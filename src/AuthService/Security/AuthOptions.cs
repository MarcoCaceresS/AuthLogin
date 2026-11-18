using System.Text;

namespace AuthService.Security;

public sealed class AuthOptions
{
    public string JwtSecret { get; set; } = "";
    public int SessionDays { get; set; } = 7;

    public TimeSpan SessionDuration => TimeSpan.FromDays(SessionDays);
    public int SessionSeconds => SessionDays * 86400;
    public byte[] SecretBytes => Encoding.UTF8.GetBytes(JwtSecret);

    public static void AddTo(IServiceCollection services)
    {
        services.AddOptions<AuthOptions>()
            .Configure<IConfiguration>((o, config) =>
            {
                o.JwtSecret = config["VIGILA_JWT_SECRETO"] ?? "";
                o.SessionDays = config.GetValue("VIGILA_CUADRILLA_SESION_DIAS", 7);
            })
            .Validate(o => o.SecretBytes.Length >= 32, "VIGILA_JWT_SECRETO debe tener al menos 32 bytes.")
            .Validate(o => o.SessionDays >= 1, "VIGILA_CUADRILLA_SESION_DIAS debe ser 1 o más.")
            .ValidateOnStart();
    }
}
