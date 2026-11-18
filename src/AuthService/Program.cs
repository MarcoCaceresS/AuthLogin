using System.Text.Json;
using AuthService.Common;
using AuthService.Data;
using AuthService.Domain;
using AuthService.Features.Cuadrilla;
using AuthService.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

AuthOptions.AddTo(builder.Services);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddSingleton<TokenReader>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddFuncionarioAuth();
// Body ausente o JSON inválido lanza BadHttpRequestException en todo ambiente → 422 datos_invalidos.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddDbContext<AuthDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Auth")));
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler(e => e.Run(ctx =>
{
    // Body JSON mal formado o de tipo incorrecto llega como BadHttpRequestException.
    var error = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var codigo = error is BadHttpRequestException ? ErrorCodes.DatosInvalidos : ErrorCodes.ErrorInterno;
    return ApiErrors.WriteAsync(ctx, codigo);
}));

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapCuadrilla();

await DevSeeder.RunAtStartupAsync(app.Services, app.Configuration);

app.Run();

public partial class Program;
