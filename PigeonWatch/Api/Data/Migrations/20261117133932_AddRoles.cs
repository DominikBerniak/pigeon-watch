using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ROLE",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NORMALIZED_NAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CONCURRENCY_STAMP = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CREATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CREATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UPDATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UPDATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ROLE", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "USER_ROLE",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ROLE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CREATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CREATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UPDATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UPDATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_ROLE", x => x.ID);
                    table.ForeignKey(
                        name: "FK_USER_ROLE_ROLE_ROLE_ID",
                        column: x => x.ROLE_ID,
                        principalTable: "ROLE",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_USER_ROLE_USER_ACCOUNT_USER_ID",
                        column: x => x.USER_ID,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ROLE_NORMALIZED_NAME",
                table: "ROLE",
                column: "NORMALIZED_NAME",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USER_ROLE_ROLE_ID",
                table: "USER_ROLE",
                column: "ROLE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USER_ROLE_USER_ID_ROLE_ID",
                table: "USER_ROLE",
                columns: new[] { "USER_ID", "ROLE_ID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USER_ROLE");

            migrationBuilder.DropTable(
                name: "ROLE");
        }
    }
}
