using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
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
                    wolfgate_feedback_report_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    steps = table.Column<string>(type: "text", nullable: true),
                    expected = table.Column<string>(type: "text", nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    frustrations = table.Column<string>(type: "text", nullable: true),
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    round_id = table.Column<int>(type: "integer", nullable: false),
                    server_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    build_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    context = table.Column<string>(type: "jsonb", nullable: false),
                    screenshot = table.Column<byte[]>(type: "bytea", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    staff_message = table.Column<string>(type: "text", nullable: true),
                    status_changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notify_player = table.Column<bool>(type: "boolean", nullable: false)
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
