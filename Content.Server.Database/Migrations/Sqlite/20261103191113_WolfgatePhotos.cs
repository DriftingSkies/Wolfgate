using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgatePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_photo",
                columns: table => new
                {
                    hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    png = table.Column<byte[]>(type: "BLOB", nullable: false),
                    width = table.Column<int>(type: "INTEGER", nullable: false),
                    height = table.Column<int>(type: "INTEGER", nullable: false),
                    author_user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    round_id = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    unused_since = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_photo", x => x.hash);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_photo");
        }
    }
}
