using System.Net;
using AuthService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthService.Tests;

/// <summary>
/// Login o renovar en medio de una desactivación: la otra transacción tiene tomada la fila del usuario.
/// El servicio debe esperar a que termine y ver la cuenta ya desactivada.
/// </summary>
public sealed class ConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_durante_desactivacion_no_deja_sesion_abierta()
    {
        var user = await TestUsers.CreateAsync(factory);

        var res = await DuringDeactivation(user.Id, () => _client.LoginAsync(user.Email, user.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("credenciales_invalidas", await res.CodigoAsync());
        await using var db = factory.NewDb();
        Assert.False(await db.UserSessions.AnyAsync(s => s.UserId == user.Id && s.RevokedAt == null));
    }

    [Fact]
    public async Task Renovar_durante_desactivacion_responde_cuenta_inactiva()
    {
        var user = await TestUsers.CreateAsync(factory);
        var token = await _client.LoginTokenAsync(user);

        var res = await DuringDeactivation(user.Id, () => _client.PostWithTokenAsync($"{Api.Base}/sesion/renovar", token));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("cuenta_inactiva", await res.CodigoAsync());
    }

    /// <summary>Hace lo mismo que el admin al desactivar, pero deja la transacción abierta mientras corre la request.</summary>
    private async Task<HttpResponseMessage> DuringDeactivation(Guid userId, Func<Task<HttpResponseMessage>> request)
    {
        string connectionString;
        await using (var db = factory.NewDb())
            connectionString = db.Database.GetConnectionString()!;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Exec(conn, tx, """UPDATE "Users" SET "IsActive" = false WHERE "Id" = @id""", userId);

        var pending = request();
        await Task.Delay(TimeSpan.FromMilliseconds(700));

        await Exec(conn, tx, """UPDATE "UserSessions" SET "RevokedAt" = now() WHERE "UserId" = @id AND "RevokedAt" IS NULL""", userId);
        await tx.CommitAsync();
        return await pending;
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, Guid id)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
