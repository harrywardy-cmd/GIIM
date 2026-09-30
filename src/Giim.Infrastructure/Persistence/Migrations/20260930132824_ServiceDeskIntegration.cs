using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Scaffolded (new columns and tables only), plus: existing checklists and device requests count as already reported
    /// to their tickets, so connecting ServiceDesk Plus doesn't post a burst of notes about old changes.
    /// </remarks>
    public partial class ServiceDeskIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServiceDeskNotifiedStatus",
                table: "DeviceRequests",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceDeskNotifiedStatus",
                table: "Cases",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceDeskRequestKey",
                table: "Cases",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceDeskInboundEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceDeskInboundEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceDeskInboundEvents_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceDeskUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeviceRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceDeskUpdates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_ServiceDeskRequestKey",
                table: "Cases",
                column: "ServiceDeskRequestKey");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDeskInboundEvents_CaseId",
                table: "ServiceDeskInboundEvents",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDeskInboundEvents_RequestKey",
                table: "ServiceDeskInboundEvents",
                column: "RequestKey");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDeskInboundEvents_Status_NextAttemptAt",
                table: "ServiceDeskInboundEvents",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDeskUpdates_DisplayId",
                table: "ServiceDeskUpdates",
                column: "DisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDeskUpdates_Status_NextAttemptAt",
                table: "ServiceDeskUpdates",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.Sql("UPDATE Cases SET ServiceDeskNotifiedStatus = Status;");
            migrationBuilder.Sql("UPDATE DeviceRequests SET ServiceDeskNotifiedStatus = Status;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceDeskInboundEvents");

            migrationBuilder.DropTable(
                name: "ServiceDeskUpdates");

            migrationBuilder.DropIndex(
                name: "IX_Cases_ServiceDeskRequestKey",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ServiceDeskNotifiedStatus",
                table: "DeviceRequests");

            migrationBuilder.DropColumn(
                name: "ServiceDeskNotifiedStatus",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ServiceDeskRequestKey",
                table: "Cases");
        }
    }
}
