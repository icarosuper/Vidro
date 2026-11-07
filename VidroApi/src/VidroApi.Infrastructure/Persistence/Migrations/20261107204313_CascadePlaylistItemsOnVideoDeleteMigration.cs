using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VidroApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CascadePlaylistItemsOnVideoDeleteMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_playlist_items_videos_video_id",
                table: "playlist_items");

            // Items whose video was deleted under the old SetNull rule: they cannot satisfy the
            // NOT NULL below, and under the new rule they should not exist.
            migrationBuilder.Sql("DELETE FROM playlist_items WHERE video_id IS NULL;");

            // The old counter never dropped on a video delete, so it drifted above the items.
            // Under the new rule the count is the items, so recount every playlist once.
            migrationBuilder.Sql("""
                UPDATE playlists
                SET video_count = (SELECT COUNT(*) FROM playlist_items WHERE playlist_items.playlist_id = playlists.id);
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "video_id",
                table: "playlist_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_playlist_items_videos_video_id",
                table: "playlist_items",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        // Restores the schema only: the deleted items and the old counts are not brought back.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_playlist_items_videos_video_id",
                table: "playlist_items");

            migrationBuilder.AlterColumn<Guid>(
                name: "video_id",
                table: "playlist_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_playlist_items_videos_video_id",
                table: "playlist_items",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
