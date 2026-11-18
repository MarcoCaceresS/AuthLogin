using System.Text.Json;
using AuthService.Common;
using AuthService.Data;
using AuthService.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

AuthOptions.AddTo(builder.Services);
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

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
