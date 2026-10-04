using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSmokeCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SMOKE_CHECK");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SMOKE_CHECK",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CREATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CREATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UPDATE_DATE = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UPDATE_USER = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SMOKE_CHECK", x => x.ID);
                });
        }
    }
}
