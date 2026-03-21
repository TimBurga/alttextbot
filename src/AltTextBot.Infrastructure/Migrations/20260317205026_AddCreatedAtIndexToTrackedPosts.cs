using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AltTextBot.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAtIndexToTrackedPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TrackedPosts_CreatedAt",
                table: "TrackedPosts",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackedPosts_CreatedAt",
                table: "TrackedPosts");
        }
    }
}
