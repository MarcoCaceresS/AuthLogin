using AuthService.Data;
using AuthService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuthService.Tests;

/// <summary>Usa su propia base: baja y sube migraciones.</summary>
public sealed class MigrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Quitar_rol_usuario_borra_vecinos_con_sus_sesiones_e_ingresos()
    {
        await using var db = factory.NewDb();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("InitialSchema");

        var vecino = Guid.NewGuid();
        var session = Guid.NewGuid();
        var funcionario = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Users" ("Id","Email","PasswordHash","FirstName","LastName","IsEmailVerified","IsActive","CreatedAt","RoleId")
            VALUES ({vecino},'vecino@victoria.cl','x','Rosa','Díaz',true,true,now(),4),
                   ({funcionario},'ana@victoria.cl','x','Ana','Pérez',true,true,now(),1);
            INSERT INTO "UserSessions" ("Id","UserId","TokenHash","ExpiresAt","CreatedAt")
            VALUES ({session},{vecino},repeat('0',64),now(),now());
            INSERT INTO "LoginRecords" ("UserId","SessionId","OccurredAt") VALUES ({vecino},{session},now());
            """);

        await migrator.MigrateAsync();

        Assert.False(await db.Roles.AnyAsync(r => r.Name == "usuario"));
        Assert.False(await db.Users.AnyAsync(u => u.Id == vecino));
        Assert.False(await db.UserSessions.AnyAsync(s => s.Id == session));
        Assert.False(await db.LoginRecords.AnyAsync(r => r.UserId == vecino));
        Assert.True(await db.Users.AnyAsync(u => u.Id == funcionario));
    }
}
