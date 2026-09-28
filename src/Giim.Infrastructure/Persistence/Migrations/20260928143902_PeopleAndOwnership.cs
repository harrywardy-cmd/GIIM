using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PeopleAndOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncedAt",
                table: "People",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedBy",
                table: "Assignments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Assignments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToPersonId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyDepartment",
                table: "Assets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_AssignedToPersonId",
                table: "Assets",
                column: "AssignedToPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_People_AssignedToPersonId",
                table: "Assets",
                column: "AssignedToPersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_Assets_AssetId",
                table: "Assignments",
                column: "AssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_People_PersonId",
                table: "Assignments",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_People_AssignedToPersonId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_Assets_AssetId",
                table: "Assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_People_PersonId",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_Assets_AssignedToPersonId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AssignedBy",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "AssignedToPersonId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "LegacyDepartment",
                table: "Assets");
        }
    }
}
