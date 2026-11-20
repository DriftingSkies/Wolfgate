using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgatePaintings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_painting",
                columns: table => new
                {
                    hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    width = table.Column<int>(type: "INTEGER", nullable: false),
                    height = table.Column<int>(type: "INTEGER", nullable: false),
                    pixels = table.Column<byte[]>(type: "BLOB", nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    artist = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    author_user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scanned_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    blocked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_painting", x => x.hash);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_painting");
        }
    }
}
