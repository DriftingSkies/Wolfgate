using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateAdminObserver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_admin_observer",
                columns: table => new
                {
                    wolfgate_admin_observer_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    data = table.Column<string>(type: "TEXT", nullable: false),
                    saved = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_admin_observer", x => x.wolfgate_admin_observer_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_admin_observer_player_user_id",
                table: "wolfgate_admin_observer",
                column: "player_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_admin_observer");
        }
    }
}
