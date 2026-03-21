using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AltTextBot.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubscriberDid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FirehoseStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastTimeUs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirehoseStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscribers",
                columns: table => new
                {
                    Did = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Handle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SubscribedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastScoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscribers", x => x.Did);
                });

            migrationBuilder.CreateTable(
                name: "TrackedPosts",
                columns: table => new
                {
                    AtUri = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SubscriberDid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HasImages = table.Column<bool>(type: "boolean", nullable: false),
                    ImageCount = table.Column<int>(type: "integer", nullable: false),
                    AltTextCount = table.Column<int>(type: "integer", nullable: false),
                    AllImagesHaveAlt = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedPosts", x => x.AtUri);
                    table.ForeignKey(
                        name: "FK_TrackedPosts_Subscribers_SubscriberDid",
                        column: x => x.SubscriberDid,
                        principalTable: "Subscribers",
                        principalColumn: "Did",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_SubscriberDid",
                table: "AuditLogs",
                column: "SubscriberDid");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedPosts_SubscriberDid_CreatedAt",
                table: "TrackedPosts",
                columns: new[] { "SubscriberDid", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "FirehoseStates");

            migrationBuilder.DropTable(
                name: "TrackedPosts");

            migrationBuilder.DropTable(
                name: "Subscribers");
        }
    }
}
