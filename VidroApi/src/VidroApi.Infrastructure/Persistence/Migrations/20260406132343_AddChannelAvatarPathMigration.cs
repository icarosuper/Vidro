using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VidroApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAvatarPathMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarPath",
                table: "channels",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarPath",
                table: "channels");
        }
    }
}
