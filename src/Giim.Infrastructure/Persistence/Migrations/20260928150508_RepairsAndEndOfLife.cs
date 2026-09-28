using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairsAndEndOfLife : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataSanitisation",
                table: "Assets",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisposalCertificate",
                table: "Assets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisposalCompany",
                table: "Assets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisposalMethod",
                table: "Assets",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DisposedOn",
                table: "Assets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                table: "Assets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetirementReason",
                table: "Assets",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Repairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedFromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Fault = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Vendor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WarrantyClaim = table.Column<bool>(type: "bit", nullable: false),
                    VendorReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SentOn = table.Column<DateOnly>(type: "date", nullable: true),
                    OpenedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TicketNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Diagnosis = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WorkPerformed = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Repairs_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_DisposalCertificate",
                table: "Assets",
                column: "DisposalCertificate");

            migrationBuilder.CreateIndex(
                name: "IX_Repairs_AssetId_OpenedAt",
                table: "Repairs",
                columns: new[] { "AssetId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Repairs_TicketNumber",
                table: "Repairs",
                column: "TicketNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Repairs_VendorReference",
                table: "Repairs",
                column: "VendorReference");

            migrationBuilder.CreateIndex(
                name: "UX_Repairs_OneOpenPerAsset",
                table: "Repairs",
                column: "AssetId",
                unique: true,
                filter: "[CompletedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Repairs");

            migrationBuilder.DropIndex(
                name: "IX_Assets_DisposalCertificate",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DataSanitisation",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DisposalCertificate",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DisposalCompany",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DisposalMethod",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DisposedOn",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "RetirementReason",
                table: "Assets");
        }
    }
}
