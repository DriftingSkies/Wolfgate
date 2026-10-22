using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateFeedbackReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_feedback_report",
                columns: table => new
                {
                    wolfgate_feedback_report_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    steps = table.Column<string>(type: "TEXT", nullable: true),
                    expected = table.Column<string>(type: "TEXT", nullable: true),
                    rating = table.Column<int>(type: "INTEGER", nullable: true),
                    frustrations = table.Column<string>(type: "TEXT", nullable: true),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    player_name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    round_id = table.Column<int>(type: "INTEGER", nullable: false),
                    server_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    build_version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    context = table.Column<string>(type: "jsonb", nullable: false),
                    screenshot = table.Column<byte[]>(type: "BLOB", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    staff_message = table.Column<string>(type: "TEXT", nullable: true),
                    status_changed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    notify_player = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_feedback_report", x => x.wolfgate_feedback_report_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_feedback_report_player_user_id_created_at",
                table: "wolfgate_feedback_report",
                columns: new[] { "player_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_feedback_report_status",
                table: "wolfgate_feedback_report",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_feedback_report");
        }
    }
}
