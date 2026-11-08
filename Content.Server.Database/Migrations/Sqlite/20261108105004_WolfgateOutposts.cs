using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateOutposts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_outpost_save",
                columns: table => new
                {
                    wolfgate_outpost_save_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    slot = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    tiles = table.Column<int>(type: "INTEGER", nullable: false),
                    entities = table.Column<int>(type: "INTEGER", nullable: false),
                    appraisal = table.Column<int>(type: "INTEGER", nullable: false),
                    data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    saved_round = table.Column<int>(type: "INTEGER", nullable: false),
                    loaded_round = table.Column<int>(type: "INTEGER", nullable: false),
                    saved = table.Column<DateTime>(type: "TEXT", nullable: false),
                    lineage = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_outpost_save", x => x.wolfgate_outpost_save_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_outpost_save_lineage",
                table: "wolfgate_outpost_save",
                column: "lineage");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_outpost_save_player_user_id_slot",
                table: "wolfgate_outpost_save",
                columns: new[] { "player_user_id", "slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_outpost_save");
        }
    }
}
