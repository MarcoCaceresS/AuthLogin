using AuthService.Domain;
using AuthService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Tests;

public sealed class DatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Migracion_crea_los_cuatro_roles()
    {
        await using var db = factory.NewDb();

        var roles = await db.Roles.Select(r => r.Name).OrderBy(n => n).ToListAsync();

        Assert.Equal(["cuadrilla", "funcionario", "superadmin", "usuario"], roles);
    }

    [Fact]
    public async Task Email_duplicado_falla()
    {
        await using var db = factory.NewDb();
        db.Users.Add(NewUser("dup@victoria.cl"));
        await db.SaveChangesAsync();

        await using var db2 = factory.NewDb();
        db2.Users.Add(NewUser("dup@victoria.cl"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    private static User NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = "x",
        FirstName = "Ana",
        LastName = "Pérez",
        IsActive = true,
        IsEmailVerified = true,
        CreatedAt = DateTimeOffset.UtcNow,
        RoleId = 1,
    };
}
