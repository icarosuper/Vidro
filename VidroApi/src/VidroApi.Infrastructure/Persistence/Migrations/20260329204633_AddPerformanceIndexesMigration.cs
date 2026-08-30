using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VidroApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexesMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_comments_parent_comment_id",
                table: "comments");

            migrationBuilder.DropIndex(
                name: "IX_comments_video_id",
                table: "comments");

            migrationBuilder.CreateIndex(
                name: "IX_videos_status_visibility",
                table: "videos",
                columns: new[] { "status", "visibility" });

            migrationBuilder.CreateIndex(
                name: "IX_comments_parent_comment_id_created_at",
                table: "comments",
                columns: new[] { "parent_comment_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_comments_video_id_parent_comment_id",
                table: "comments",
                columns: new[] { "video_id", "parent_comment_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_videos_status_visibility",
                table: "videos");

            migrationBuilder.DropIndex(
                name: "IX_comments_parent_comment_id_created_at",
                table: "comments");

            migrationBuilder.DropIndex(
                name: "IX_comments_video_id_parent_comment_id",
                table: "comments");

            migrationBuilder.CreateIndex(
                name: "IX_comments_parent_comment_id",
                table: "comments",
                column: "parent_comment_id");

            migrationBuilder.CreateIndex(
                name: "IX_comments_video_id",
                table: "comments",
                column: "video_id");
        }
    }
}
