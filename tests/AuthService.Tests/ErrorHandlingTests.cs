using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace AuthService.Tests;

public sealed class ErrorHandlingTests
{
    [Theory]
    [InlineData("corto-de-10", "7")]
    [InlineData("un-secreto-de-prueba-de-al-menos-32-bytes!", "0")]
    public void Configuracion_invalida_impide_arrancar(string secreto, string dias)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Auth", "Host=localhost;Database=none");
            b.UseSetting("VIGILA_JWT_SECRETO", secreto);
            b.UseSetting("VIGILA_CUADRILLA_SESION_DIAS", dias);
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.IsType<OptionsValidationException>(ex.GetBaseException());
    }
}
