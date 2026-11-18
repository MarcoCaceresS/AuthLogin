using AuthService.Data;
using AuthService.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Tests.Infrastructure;

public sealed record TestUser(Guid Id, string Email, string Password, string Rol);

/// <summary>Crea usuarios con email único para que los tests no choquen entre sí.</summary>
public static class TestUsers
{
    public const string Password = "clave-de-prueba-1";

    public static async Task<TestUser> CreateAsync(
        ApiFactory factory, string rol = RoleNames.Funcionario,
        bool isActive = true, bool isEmailVerified = true)
    {
        var email = $"{rol}-{Guid.NewGuid():N}@victoria.cl";
        await using var db = factory.NewDb();
        var hasher = factory.Services.GetRequiredService<IPasswordHasher<User>>();
        var seed = new SeedUser
        {
            Email = email, Password = Password, FirstName = "Ana", LastName = "Pérez",
            Rol = rol, IsActive = isActive, IsEmailVerified = isEmailVerified,
        };
        await DevSeeder.SeedAsync(db, hasher, [seed], TimeProvider.System, default);
        var id = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return new TestUser(id, email, Password, rol);
    }
}
