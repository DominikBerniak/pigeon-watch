using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSightingThreadReadMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OWNER_LAST_READ_AT",
                table: "SIGHTING_THREAD",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PARTICIPANT_LAST_READ_AT",
                table: "SIGHTING_THREAD",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OWNER_LAST_READ_AT",
                table: "SIGHTING_THREAD");

            migrationBuilder.DropColumn(
                name: "PARTICIPANT_LAST_READ_AT",
                table: "SIGHTING_THREAD");
        }
    }
}
