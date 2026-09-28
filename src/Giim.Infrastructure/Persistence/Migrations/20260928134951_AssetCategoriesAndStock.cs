using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetCategoriesAndStock : Migration
    {
        /// <inheritdoc />
        // Hand-edited: the scaffolded version dropped Assets.Category before converting it, losing every
        // asset's category. This version creates the categories, converts the data, then drops the old columns.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsIntuneManaged = table.Column<bool>(type: "bit", nullable: false),
                    ReturnOnOffboarding = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReorderLevel = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ServiceDeskRequestId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AssetCategories",
                columns: new[] { "Id", "CreatedAt", "IsActive", "IsIntuneManaged", "Name", "ReturnOnOffboarding", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Laptop", true, null },
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Desktop", true, null },
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "Monitor", true, null },
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000004"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "Dock", true, null },
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000005"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Phone", true, null },
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000006"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Tablet", true, null },
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000007"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "Peripheral", true, null },
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000008"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "Other", true, null },
                    { new Guid("0c7f6a1e-0001-4000-8000-000000000009"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "Monitor mount", true, null }
                });

            // Assets: add the link as nullable, fill it from the old text column, then make it required.
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE a SET a.CategoryId = COALESCE(c.Id, '0c7f6a1e-0001-4000-8000-000000000008') -- unmatched -> Other
                FROM Assets a
                LEFT JOIN AssetCategories c ON c.Name = a.Category;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Assets");

            // Profile items: same conversion for hardware items.
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "ProfileItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StockItemId",
                table: "ProfileItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE p SET p.CategoryId = c.Id
                FROM ProfileItems p
                JOIN AssetCategories c ON c.Name = p.HardwareCategory;
                """);

            migrationBuilder.DropColumn(
                name: "HardwareCategory",
                table: "ProfileItems");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_CategoryId",
                table: "Assets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_Name",
                table: "AssetCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_Name",
                table: "StockItems",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CreatedAt",
                table: "StockMovements",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockItemId_Location",
                table: "StockMovements",
                columns: new[] { "StockItemId", "Location" });

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_AssetCategories_CategoryId",
                table: "Assets",
                column: "CategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        // Rolls back to the old text columns. Categories added after this migration become "Other",
        // and all stock data is removed.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Assets",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<string>(
                name: "HardwareCategory",
                table: "ProfileItems",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE a SET a.Category = c.Name
                FROM Assets a
                JOIN AssetCategories c ON c.Id = a.CategoryId
                WHERE c.Name IN ('Laptop','Desktop','Monitor','Dock','Phone','Tablet','Peripheral','Other');

                UPDATE p SET p.HardwareCategory = c.Name
                FROM ProfileItems p
                JOIN AssetCategories c ON c.Id = p.CategoryId
                WHERE c.Name IN ('Laptop','Desktop','Monitor','Dock','Phone','Tablet','Peripheral','Other');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_AssetCategories_CategoryId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_CategoryId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "ProfileItems");

            migrationBuilder.DropColumn(
                name: "StockItemId",
                table: "ProfileItems");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockItems");

            migrationBuilder.DropTable(
                name: "AssetCategories");
        }
    }
}
