using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PigeonWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSightingRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "REMOVED_AT",
                table: "SIGHTING",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "REMOVED_BY",
                table: "SIGHTING",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SIGHTING_REMOVED_BY",
                table: "SIGHTING",
                column: "REMOVED_BY");

            migrationBuilder.AddForeignKey(
                name: "FK_SIGHTING_USER_ACCOUNT_REMOVED_BY",
                table: "SIGHTING",
                column: "REMOVED_BY",
                principalTable: "USER_ACCOUNT",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SIGHTING_USER_ACCOUNT_REMOVED_BY",
                table: "SIGHTING");

            migrationBuilder.DropIndex(
                name: "IX_SIGHTING_REMOVED_BY",
                table: "SIGHTING");

            migrationBuilder.DropColumn(
                name: "REMOVED_AT",
                table: "SIGHTING");

            migrationBuilder.DropColumn(
                name: "REMOVED_BY",
                table: "SIGHTING");
        }
    }
}
