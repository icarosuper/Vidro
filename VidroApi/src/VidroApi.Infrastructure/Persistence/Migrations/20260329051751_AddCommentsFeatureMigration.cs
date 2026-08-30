using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VidroApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentsFeatureMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommentCount",
                table: "videos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "dislike_count",
                table: "comments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "like_count",
                table: "comments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_comment_id",
                table: "comments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reply_count",
                table: "comments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "comment_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comment_reactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_comment_reactions_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comment_reactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_comments_parent_comment_id",
                table: "comments",
                column: "parent_comment_id");

            migrationBuilder.CreateIndex(
                name: "IX_comment_reactions_comment_id_user_id",
                table: "comment_reactions",
                columns: new[] { "comment_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comment_reactions_user_id",
                table: "comment_reactions",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_comments_comments_parent_comment_id",
                table: "comments",
                column: "parent_comment_id",
                principalTable: "comments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_comments_comments_parent_comment_id",
                table: "comments");

            migrationBuilder.DropTable(
                name: "comment_reactions");

            migrationBuilder.DropIndex(
                name: "IX_comments_parent_comment_id",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "CommentCount",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "dislike_count",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "like_count",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "parent_comment_id",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "reply_count",
                table: "comments");
        }
    }
}
