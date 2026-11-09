using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateCensus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_census",
                columns: table => new
                {
                    wolfgate_census_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    questions = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    anonymous = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    created_by = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    opened_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    closes_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    closed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_census", x => x.wolfgate_census_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_census_response",
                columns: table => new
                {
                    wolfgate_census_response_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    census_id = table.Column<int>(type: "INTEGER", nullable: false),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    answers = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_census_response", x => x.wolfgate_census_response_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_census_response_wolfgate_census_census_id",
                        column: x => x.census_id,
                        principalTable: "wolfgate_census",
                        principalColumn: "wolfgate_census_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_census_response_census_id",
                table: "wolfgate_census_response",
                column: "census_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_census_response_census_id_player_user_id",
                table: "wolfgate_census_response",
                columns: new[] { "census_id", "player_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_census_response");

            migrationBuilder.DropTable(
                name: "wolfgate_census");
        }
    }
}
