using AuthService.Domain;
using AuthService.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

public sealed class SeedUser
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Rol { get; set; } = RoleNames.Funcionario;
    public bool IsActive { get; set; } = true;
    public bool IsEmailVerified { get; set; } = true;
}

/// <summary>Carga usuarios iniciales. Es idempotente: no toca usuarios que ya existen.</summary>
public static class DevSeeder
{
    /// <summary>Migra (si se pide) y siembra según configuración. Corre al arrancar.</summary>
    public static async Task RunAtStartupAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AuthDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();
        var time = sp.GetRequiredService<TimeProvider>();

        if (config.GetValue("Database:MigrateOnStartup", false))
            await db.Database.MigrateAsync();

        if (config.GetValue("Seed:Enabled", false))
        {
            var users = config.GetSection("Seed:Users").Get<List<SeedUser>>() ?? [];
            await SeedAsync(db, hasher, users, time, default);
        }

        var admin = config.GetSection("Seed:Admin").Get<SeedUser>();
        if (!string.IsNullOrWhiteSpace(admin?.Email))
            await SeedAdminAsync(db, hasher, admin, time, default);
    }

    public static async Task SeedAsync(
        AuthDbContext db, IPasswordHasher<User> hasher, IReadOnlyList<SeedUser> users,
        TimeProvider time, CancellationToken ct)
    {
        foreach (var seed in users)
        {
            var email = User.NormalizeEmail(seed.Email);
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
                continue;

            if (PasswordRules.Validate(seed.Password) is { } problema)
                throw new InvalidOperationException($"Seed de {email}: {problema}");

            var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == seed.Rol, ct)
                       ?? throw new InvalidOperationException($"Seed de {email}: rol desconocido '{seed.Rol}'.");

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                FirstName = seed.FirstName.Trim(),
                LastName = seed.LastName.Trim(),
                IsActive = seed.IsActive,
                IsEmailVerified = seed.IsEmailVerified,
                CreatedAt = time.GetUtcNow(),
                RoleId = role.Id,
            };
            user.PasswordHash = hasher.HashPassword(user, seed.Password);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Crea el primer superadmin solo si todavía no existe ninguno.</summary>
    public static async Task SeedAdminAsync(
        AuthDbContext db, IPasswordHasher<User> hasher, SeedUser admin,
        TimeProvider time, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Role.Name == RoleNames.Superadmin, ct))
            return;

        admin.Rol = RoleNames.Superadmin;
        admin.IsActive = true;
        admin.IsEmailVerified = true;
        await SeedAsync(db, hasher, [admin], time, ct);
    }
}
