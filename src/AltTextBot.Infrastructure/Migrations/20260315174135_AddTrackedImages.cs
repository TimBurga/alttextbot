using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AltTextBot.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PostedAt",
                table: "TrackedPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrackedImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostAtUri = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    BlobCid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HasAlt = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedImages_TrackedPosts_PostAtUri",
                        column: x => x.PostAtUri,
                        principalTable: "TrackedPosts",
                        principalColumn: "AtUri",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedImages_PostAtUri",
                table: "TrackedImages",
                column: "PostAtUri");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedImages");

            migrationBuilder.DropColumn(
                name: "PostedAt",
                table: "TrackedPosts");
        }
    }
}
