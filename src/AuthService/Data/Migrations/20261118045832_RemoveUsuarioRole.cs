using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUsuarioRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los vecinos usan otra app: se borran con su historial antes de quitar el rol (FK Restrict).
            migrationBuilder.Sql("""
                DELETE FROM "LoginRecords" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "RoleId" = 4);
                DELETE FROM "UserSessions" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "RoleId" = 4);
                DELETE FROM "Users" WHERE "RoleId" = 4;
                """);

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name" },
                values: new object[] { 4, "usuario" });
        }
    }
}
