using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateHideouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_hideout_ship",
                columns: table => new
                {
                    wolfgate_hideout_ship_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    value = table.Column<int>(type: "INTEGER", nullable: false),
                    data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    saved_round = table.Column<int>(type: "INTEGER", nullable: false),
                    saved = table.Column<DateTime>(type: "TEXT", nullable: false),
                    lost = table.Column<bool>(type: "INTEGER", nullable: false),
                    insured_value = table.Column<int>(type: "INTEGER", nullable: false),
                    insured_data = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_hideout_ship", x => x.wolfgate_hideout_ship_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_hideout_ship_player_user_id",
                table: "wolfgate_hideout_ship",
                column: "player_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_hideout_ship");
        }
    }
}
