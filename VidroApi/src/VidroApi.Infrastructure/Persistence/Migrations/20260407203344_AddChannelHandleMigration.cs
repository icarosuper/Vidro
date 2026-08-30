using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VidroApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelHandleMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_channels_user_id",
                table: "channels");

            migrationBuilder.AddColumn<string>(
                name: "handle",
                table: "channels",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_channels_user_id_handle",
                table: "channels",
                columns: new[] { "user_id", "handle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_channels_user_id_handle",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "handle",
                table: "channels");

            migrationBuilder.CreateIndex(
                name: "IX_channels_user_id",
                table: "channels",
                column: "user_id");
        }
    }
}
