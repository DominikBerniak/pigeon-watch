using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSightingMessageThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SIGHTING_THREAD",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SIGHTING_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PARTICIPANT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LAST_MESSAGE_AT = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CREATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CREATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UPDATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UPDATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SIGHTING_THREAD", x => x.ID);
                    table.ForeignKey(
                        name: "FK_SIGHTING_THREAD_SIGHTING_SIGHTING_ID",
                        column: x => x.SIGHTING_ID,
                        principalTable: "SIGHTING",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SIGHTING_THREAD_USER_ACCOUNT_PARTICIPANT_ID",
                        column: x => x.PARTICIPANT_ID,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SIGHTING_MESSAGE",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    THREAD_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SENDER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TEXT = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CREATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CREATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UPDATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UPDATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SIGHTING_MESSAGE", x => x.ID);
                    table.ForeignKey(
                        name: "FK_SIGHTING_MESSAGE_SIGHTING_THREAD_THREAD_ID",
                        column: x => x.THREAD_ID,
                        principalTable: "SIGHTING_THREAD",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SIGHTING_MESSAGE_USER_ACCOUNT_SENDER_ID",
                        column: x => x.SENDER_ID,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_MESSAGE_SENDER_ID",
                table: "SIGHTING_MESSAGE",
                column: "SENDER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_MESSAGE_THREAD_ID_CREATE_DATE",
                table: "SIGHTING_MESSAGE",
                columns: new[] { "THREAD_ID", "CREATE_DATE" });

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_THREAD_PARTICIPANT_ID_LAST_MESSAGE_AT",
                table: "SIGHTING_THREAD",
                columns: new[] { "PARTICIPANT_ID", "LAST_MESSAGE_AT" });

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_THREAD_SIGHTING_ID_PARTICIPANT_ID",
                table: "SIGHTING_THREAD",
                columns: new[] { "SIGHTING_ID", "PARTICIPANT_ID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SIGHTING_MESSAGE");

            migrationBuilder.DropTable(
                name: "SIGHTING_THREAD");
        }
    }
}
