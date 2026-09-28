using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentAccessories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceivedBy",
                table: "Assignments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnCondition",
                table: "Assignments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnTicketNumber",
                table: "Assignments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnedBy",
                table: "Assignments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssignmentAccessories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    AccessoryAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StockItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentAccessories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentAccessories_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Assignments_OneActivePerAsset",
                table: "Assignments",
                column: "AssetId",
                unique: true,
                filter: "[EndedAt] IS NULL AND [AssetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAccessories_AccessoryAssetId",
                table: "AssignmentAccessories",
                column: "AccessoryAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAccessories_AssignmentId",
                table: "AssignmentAccessories",
                column: "AssignmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentAccessories");

            migrationBuilder.DropIndex(
                name: "UX_Assignments_OneActivePerAsset",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "ReceivedBy",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "ReturnCondition",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "ReturnTicketNumber",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "ReturnedBy",
                table: "Assignments");
        }
    }
}
