using AuthService.Data;
using AuthService.Domain;
using AuthService.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Tests;

public sealed class DatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private IPasswordHasher<User> Hasher => factory.Services.GetRequiredService<IPasswordHasher<User>>();

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

    [Fact]
    public async Task Seed_guarda_hash_y_no_la_clave()
    {
        await using var db = factory.NewDb();
        await DevSeeder.SeedAsync(db, Hasher, [Seed("hash@victoria.cl")], TimeProvider.System, default);

        var user = await db.Users.SingleAsync(u => u.Email == "hash@victoria.cl");

        Assert.NotEqual("clave-segura-1", user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            Hasher.VerifyHashedPassword(user, user.PasswordHash, "clave-segura-1"));
    }

    [Fact]
    public async Task Seed_es_idempotente()
    {
        await using var db = factory.NewDb();
        await DevSeeder.SeedAsync(db, Hasher, [Seed("idem@victoria.cl")], TimeProvider.System, default);
        await DevSeeder.SeedAsync(db, Hasher, [Seed("idem@victoria.cl")], TimeProvider.System, default);

        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "idem@victoria.cl"));
    }

    [Fact]
    public async Task Seed_normaliza_email()
    {
        await using var db = factory.NewDb();
        await DevSeeder.SeedAsync(db, Hasher, [Seed(" Norma@Victoria.CL ")], TimeProvider.System, default);

        Assert.True(await db.Users.AnyAsync(u => u.Email == "norma@victoria.cl"));
    }

    [Fact]
    public async Task Seed_con_clave_corta_falla()
    {
        await using var db = factory.NewDb();
        var corto = Seed("corta@victoria.cl");
        corto.Password = "1234567";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevSeeder.SeedAsync(db, Hasher, [corto], TimeProvider.System, default));
    }

    [Fact]
    public async Task SeedAdmin_crea_solo_si_no_hay_superadmin()
    {
        await using var db = factory.NewDb();

        await DevSeeder.SeedAdminAsync(db, Hasher, Seed("admin1@victoria.cl"), TimeProvider.System, default);
        await DevSeeder.SeedAdminAsync(db, Hasher, Seed("admin2@victoria.cl"), TimeProvider.System, default);

        var admins = await db.Users.Where(u => u.Role.Name == RoleNames.Superadmin).Select(u => u.Email).ToListAsync();
        Assert.Equal(["admin1@victoria.cl"], admins);
    }

    private static SeedUser Seed(string email) => new()
    {
        Email = email,
        Password = "clave-segura-1",
        FirstName = "Ana",
        LastName = "Pérez",
        Rol = RoleNames.Funcionario,
    };

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
