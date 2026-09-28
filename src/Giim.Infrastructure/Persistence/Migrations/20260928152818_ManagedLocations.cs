using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Hand-edited. The scaffolded version dropped Assets.Location before converting it (losing every asset's
    /// location) and gave stock movements an empty location id that would break the new foreign key. This version
    /// creates a managed location for every name already in use, links assets and stock movements to it, and only
    /// then drops the old text column. Stock movements keep their original location name for the audit trail.
    /// </remarks>
    public partial class ManagedLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HoldsStock = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Name",
                table: "Locations",
                column: "Name",
                unique: true);

            // One location per name in use. The column collation is case-insensitive, so "IT store room" and
            // "IT Store Room" become one location. Kind is a best guess that IT can correct on the Locations page.
            migrationBuilder.Sql("""
                WITH names AS (
                    SELECT LTRIM(RTRIM(Location)) AS Name, 0 AS FromStock FROM Assets WHERE LEN(LTRIM(RTRIM(ISNULL(Location, '')))) BETWEEN 1 AND 150
                    UNION ALL
                    SELECT LTRIM(RTRIM(Location)), 1 FROM StockMovements WHERE LEN(LTRIM(RTRIM(Location))) BETWEEN 1 AND 150
                ),
                grouped AS (
                    SELECT MIN(Name) AS Name, MAX(FromStock) AS FromStock FROM names GROUP BY Name
                ),
                kinded AS (
                    SELECT Name, FromStock,
                        CASE
                            WHEN Name LIKE N'%store%' OR Name LIKE N'%stock%' THEN N'ItStoreRoom'
                            WHEN Name LIKE N'%distribution%' OR Name LIKE N'%warehouse%' THEN N'DistributionCentre'
                            WHEN Name LIKE N'%office%' THEN N'Office'
                            WHEN Name LIKE N'%home%' OR Name LIKE N'%remote%' THEN N'Remote'
                            ELSE N'Other'
                        END AS Kind
                    FROM grouped
                )
                INSERT INTO Locations (Id, Name, Kind, Address, HoldsStock, IsActive, CreatedAt, UpdatedAt)
                SELECT NEWID(), Name, Kind, NULL,
                       CASE WHEN FromStock = 1 OR Kind IN (N'ItStoreRoom', N'DistributionCentre') THEN 1 ELSE 0 END,
                       1, SYSDATETIMEOFFSET(), NULL
                FROM kinded;
                """);

            // Assets: link to the location, then drop the text column.
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE a SET a.LocationId = l.Id
                FROM Assets a JOIN Locations l ON l.Name = LTRIM(RTRIM(a.Location));
                """);

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Assets");

            // Stock movements: link to the location; the name column stays as the historical record.
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "StockMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE m SET m.LocationId = l.Id
                FROM StockMovements m JOIN Locations l ON l.Name = LTRIM(RTRIM(m.Location));
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "LocationId",
                table: "StockMovements",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_StockItemId_Location",
                table: "StockMovements");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_LocationId",
                table: "StockMovements",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockItemId_LocationId",
                table: "StockMovements",
                columns: new[] { "StockItemId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_LocationId",
                table: "Assets",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Locations_LocationId",
                table: "Assets",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Locations_LocationId",
                table: "StockMovements",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        /// <remarks>Restores the text column from the current location names (renames are kept as the new name).</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE a SET a.Location = l.Name
                FROM Assets a JOIN Locations l ON l.Id = a.LocationId;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Locations_LocationId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Locations_LocationId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_LocationId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_StockItemId_LocationId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Assets_LocationId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Assets");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockItemId_Location",
                table: "StockMovements",
                columns: new[] { "StockItemId", "Location" });

            migrationBuilder.DropTable(
                name: "Locations");
        }
    }
}
