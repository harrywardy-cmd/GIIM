using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotsAndCloudGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CloudGroup",
                table: "ProfileItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DashboardSnapshots",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TakenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InService = table.Column<int>(type: "int", nullable: false),
                    Assigned = table.Column<int>(type: "int", nullable: false),
                    Available = table.Column<int>(type: "int", nullable: false),
                    InRepair = table.Column<int>(type: "int", nullable: false),
                    Retired = table.Column<int>(type: "int", nullable: false),
                    Disposed = table.Column<int>(type: "int", nullable: false),
                    PendingApproval = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardSnapshots", x => x.Date);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardSnapshots");

            migrationBuilder.DropColumn(
                name: "CloudGroup",
                table: "ProfileItems");
        }
    }
}
