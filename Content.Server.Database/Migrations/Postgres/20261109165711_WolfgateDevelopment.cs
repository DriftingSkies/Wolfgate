using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgateDevelopment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_world_charter",
                columns: table => new
                {
                    wolfgate_world_charter_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    world = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    granted = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rival = table.Column<int>(type: "integer", nullable: true),
                    streak = table.Column<int>(type: "integer", nullable: false),
                    last_check = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    defence = table.Column<bool>(type: "boolean", nullable: false),
                    open = table.Column<bool>(type: "boolean", nullable: false),
                    permits = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_world_charter", x => x.wolfgate_world_charter_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_world_development",
                columns: table => new
                {
                    wolfgate_world_development_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    holder = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    world = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    points = table.Column<double>(type: "double precision", nullable: false),
                    updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_world_development", x => x.wolfgate_world_development_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_world_yard",
                columns: table => new
                {
                    wolfgate_world_yard_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    world = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    x = table.Column<float>(type: "real", nullable: false),
                    y = table.Column<float>(type: "real", nullable: false),
                    berths = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    saved = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_world_yard", x => x.wolfgate_world_yard_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_yard_job",
                columns: table => new
                {
                    wolfgate_yard_job_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    world = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    design = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    progress = table.Column<float>(type: "real", nullable: false),
                    components = table.Column<int>(type: "integer", nullable: false),
                    metals = table.Column<int>(type: "integer", nullable: false),
                    updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_yard_job", x => x.wolfgate_yard_job_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_world_charter_world",
                table: "wolfgate_world_charter",
                column: "world",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_world_development_world_holder",
                table: "wolfgate_world_development",
                columns: new[] { "world", "holder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_world_yard_world",
                table: "wolfgate_world_yard",
                column: "world",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_yard_job_world",
                table: "wolfgate_yard_job",
                column: "world");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_world_charter");

            migrationBuilder.DropTable(
                name: "wolfgate_world_development");

            migrationBuilder.DropTable(
                name: "wolfgate_world_yard");

            migrationBuilder.DropTable(
                name: "wolfgate_yard_job");
        }
    }
}
