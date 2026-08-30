using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VidroApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomThumbnailPathMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_thumbnail_path",
                table: "video_artifacts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_path",
                table: "users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_thumbnail_path",
                table: "video_artifacts");

            migrationBuilder.DropColumn(
                name: "avatar_path",
                table: "users");
        }
    }
}
