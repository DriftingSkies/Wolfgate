using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgatePhotoAlbums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_photo_album",
                columns: table => new
                {
                    album_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<string>(type: "text", nullable: false),
                    round_id = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_photo_album", x => x.album_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_photo_album_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_photo_album_profile_id",
                table: "wolfgate_photo_album",
                column: "profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_photo_album");
        }
    }
}
