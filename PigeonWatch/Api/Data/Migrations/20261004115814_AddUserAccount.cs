using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "USER_ACCOUNT",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EMAIL = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NORMALIZED_EMAIL = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    USER_NAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NORMALIZED_USER_NAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DISPLAY_NAME = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NORMALIZED_DISPLAY_NAME = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PASSWORD_HASH = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SECURITY_STAMP = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CONCURRENCY_STAMP = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LOCKOUT_END = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LOCKOUT_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                    ACCESS_FAILED_COUNT = table.Column<int>(type: "int", nullable: false),
                    CREATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CREATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UPDATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UPDATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_ACCOUNT", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_NORMALIZED_DISPLAY_NAME",
                table: "USER_ACCOUNT",
                column: "NORMALIZED_DISPLAY_NAME",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_NORMALIZED_EMAIL",
                table: "USER_ACCOUNT",
                column: "NORMALIZED_EMAIL",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_NORMALIZED_USER_NAME",
                table: "USER_ACCOUNT",
                column: "NORMALIZED_USER_NAME",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USER_ACCOUNT");
        }
    }
}
