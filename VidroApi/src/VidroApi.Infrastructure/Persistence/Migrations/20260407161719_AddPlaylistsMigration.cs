using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VidroApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistsMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_channel_followers_channels_channel_id",
                table: "channel_followers");

            migrationBuilder.DropForeignKey(
                name: "FK_channel_followers_users_user_id",
                table: "channel_followers");

            migrationBuilder.DropForeignKey(
                name: "FK_channels_users_user_id",
                table: "channels");

            migrationBuilder.DropForeignKey(
                name: "FK_comment_reactions_comments_comment_id",
                table: "comment_reactions");

            migrationBuilder.DropForeignKey(
                name: "FK_comment_reactions_users_user_id",
                table: "comment_reactions");

            migrationBuilder.DropForeignKey(
                name: "FK_comments_comments_parent_comment_id",
                table: "comments");

            migrationBuilder.DropForeignKey(
                name: "FK_comments_users_user_id",
                table: "comments");

            migrationBuilder.DropForeignKey(
                name: "FK_comments_videos_video_id",
                table: "comments");

            migrationBuilder.DropForeignKey(
                name: "FK_reactions_users_user_id",
                table: "reactions");

            migrationBuilder.DropForeignKey(
                name: "FK_reactions_videos_video_id",
                table: "reactions");

            migrationBuilder.DropForeignKey(
                name: "FK_refresh_tokens_users_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_video_artifacts_videos_video_id",
                table: "video_artifacts");

            migrationBuilder.DropForeignKey(
                name: "FK_video_metadata_videos_video_id",
                table: "video_metadata");

            migrationBuilder.DropForeignKey(
                name: "FK_videos_channels_channel_id",
                table: "videos");

            migrationBuilder.CreateTable(
                name: "playlists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    video_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playlists", x => x.id);
                    table.ForeignKey(
                        name: "FK_playlists_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_playlists_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    playlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playlist_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_playlist_items_playlists_playlist_id",
                        column: x => x.playlist_id,
                        principalTable: "playlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_playlist_items_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_playlist_items_playlist_id_created_at",
                table: "playlist_items",
                columns: new[] { "playlist_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_playlist_items_playlist_id_video_id",
                table: "playlist_items",
                columns: new[] { "playlist_id", "video_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_playlist_items_video_id",
                table: "playlist_items",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "IX_playlists_channel_id",
                table: "playlists",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_playlists_user_id",
                table: "playlists",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_channel_followers_channels_channel_id",
                table: "channel_followers",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_channel_followers_users_user_id",
                table: "channel_followers",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_channels_users_user_id",
                table: "channels",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_comment_reactions_comments_comment_id",
                table: "comment_reactions",
                column: "comment_id",
                principalTable: "comments",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_comment_reactions_users_user_id",
                table: "comment_reactions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_comments_comments_parent_comment_id",
                table: "comments",
                column: "parent_comment_id",
                principalTable: "comments",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_comments_users_user_id",
                table: "comments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_comments_videos_video_id",
                table: "comments",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_reactions_users_user_id",
                table: "reactions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_reactions_videos_video_id",
                table: "reactions",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_refresh_tokens_users_user_id",
                table: "refresh_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_video_artifacts_videos_video_id",
                table: "video_artifacts",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_video_metadata_videos_video_id",
                table: "video_metadata",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_videos_channels_channel_id",
                table: "videos",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_channel_followers_channels_channel_id",
                table: "channel_followers");

            migrationBuilder.DropForeignKey(
                name: "FK_channel_followers_users_user_id",
                table: "channel_followers");

            migrationBuilder.DropForeignKey(
                name: "FK_channels_users_user_id",
                table: "channels");

            migrationBuilder.DropForeignKey(
                name: "FK_comment_reactions_comments_comment_id",
                table: "comment_reactions");

            migrationBuilder.DropForeignKey(
                name: "FK_comment_reactions_users_user_id",
                table: "comment_reactions");

            migrationBuilder.DropForeignKey(
                name: "FK_comments_comments_parent_comment_id",
                table: "comments");

            migrationBuilder.DropForeignKey(
                name: "FK_comments_users_user_id",
                table: "comments");

            migrationBuilder.DropForeignKey(
                name: "FK_comments_videos_video_id",
                table: "comments");

            migrationBuilder.DropForeignKey(
                name: "FK_reactions_users_user_id",
                table: "reactions");

            migrationBuilder.DropForeignKey(
                name: "FK_reactions_videos_video_id",
                table: "reactions");

            migrationBuilder.DropForeignKey(
                name: "FK_refresh_tokens_users_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_video_artifacts_videos_video_id",
                table: "video_artifacts");

            migrationBuilder.DropForeignKey(
                name: "FK_video_metadata_videos_video_id",
                table: "video_metadata");

            migrationBuilder.DropForeignKey(
                name: "FK_videos_channels_channel_id",
                table: "videos");

            migrationBuilder.DropTable(
                name: "playlist_items");

            migrationBuilder.DropTable(
                name: "playlists");

            migrationBuilder.AddForeignKey(
                name: "FK_channel_followers_channels_channel_id",
                table: "channel_followers",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_channel_followers_users_user_id",
                table: "channel_followers",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_channels_users_user_id",
                table: "channels",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_comment_reactions_comments_comment_id",
                table: "comment_reactions",
                column: "comment_id",
                principalTable: "comments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_comment_reactions_users_user_id",
                table: "comment_reactions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_comments_comments_parent_comment_id",
                table: "comments",
                column: "parent_comment_id",
                principalTable: "comments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_comments_users_user_id",
                table: "comments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_comments_videos_video_id",
                table: "comments",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reactions_users_user_id",
                table: "reactions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reactions_videos_video_id",
                table: "reactions",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_refresh_tokens_users_user_id",
                table: "refresh_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_video_artifacts_videos_video_id",
                table: "video_artifacts",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_video_metadata_videos_video_id",
                table: "video_metadata",
                column: "video_id",
                principalTable: "videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_videos_channels_channel_id",
                table: "videos",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
