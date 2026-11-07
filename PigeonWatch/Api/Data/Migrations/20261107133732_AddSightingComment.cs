using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSightingComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SIGHTING_COMMENT",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SIGHTING_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AUTHOR_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TEXT = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EDITED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DELETED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CREATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UPDATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UPDATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SIGHTING_COMMENT", x => x.ID);
                    table.ForeignKey(
                        name: "FK_SIGHTING_COMMENT_SIGHTING_SIGHTING_ID",
                        column: x => x.SIGHTING_ID,
                        principalTable: "SIGHTING",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SIGHTING_COMMENT_USER_ACCOUNT_AUTHOR_ID",
                        column: x => x.AUTHOR_ID,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_COMMENT_AUTHOR_ID_CREATE_DATE",
                table: "SIGHTING_COMMENT",
                columns: new[] { "AUTHOR_ID", "CREATE_DATE" });

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_COMMENT_SIGHTING_ID_CREATE_DATE",
                table: "SIGHTING_COMMENT",
                columns: new[] { "SIGHTING_ID", "CREATE_DATE" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SIGHTING_COMMENT");
        }
    }
}
