using AuthService.Security;

namespace AuthService.Tests;

public sealed class PasswordRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    public void Largo_minimo_8(string? password, bool valida) =>
        Assert.Equal(valida, PasswordRules.Validate(password) is null);

    [Fact]
    public void Largo_maximo_128()
    {
        Assert.Null(PasswordRules.Validate(new string('a', 128)));
        Assert.Equal("La contraseña debe tener entre 8 y 128 caracteres.", PasswordRules.Validate(new string('a', 129)));
    }
}
