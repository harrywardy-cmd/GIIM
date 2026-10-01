using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Scaffolded (new columns and a table only), plus: checklists that already exist count as their manager already
    /// emailed, so switching reminders on doesn't email managers about old starters and leavers.
    /// </remarks>
    public partial class Reminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalReminders",
                table: "DeviceRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastApprovalReminderAt",
                table: "DeviceRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReturnReminderAt",
                table: "Cases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ManagerEmailedAt",
                table: "Cases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnReminders",
                table: "Cases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ScheduledJobRuns",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastRunOn = table.Column<DateOnly>(type: "date", nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastResult = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobRuns", x => x.Name);
                });

            migrationBuilder.Sql("UPDATE Cases SET ManagerEmailedAt = CreatedAt;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledJobRuns");

            migrationBuilder.DropColumn(
                name: "ApprovalReminders",
                table: "DeviceRequests");

            migrationBuilder.DropColumn(
                name: "LastApprovalReminderAt",
                table: "DeviceRequests");

            migrationBuilder.DropColumn(
                name: "LastReturnReminderAt",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ManagerEmailedAt",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ReturnReminders",
                table: "Cases");
        }
    }
}
