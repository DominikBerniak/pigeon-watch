using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSighting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SIGHTING",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REPORTER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NAME = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    BEHAVIOUR = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LATITUDE = table.Column<double>(type: "float", nullable: false),
                    LONGITUDE = table.Column<double>(type: "float", nullable: false),
                    STATUS = table.Column<int>(type: "int", nullable: false),
                    CRITICALITY = table.Column<int>(type: "int", nullable: false),
                    CREATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CREATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UPDATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UPDATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SIGHTING", x => x.ID);
                    table.ForeignKey(
                        name: "FK_SIGHTING_USER_ACCOUNT_REPORTER_ID",
                        column: x => x.REPORTER_ID,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_LATITUDE_LONGITUDE",
                table: "SIGHTING",
                columns: new[] { "LATITUDE", "LONGITUDE" });

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_REPORTER_ID_CREATE_DATE",
                table: "SIGHTING",
                columns: new[] { "REPORTER_ID", "CREATE_DATE" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SIGHTING");
        }
    }
}
