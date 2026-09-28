using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntuneDevicesAndSyncRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntuneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OperatingSystem = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserPrincipalName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ComplianceState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastSyncDateTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EnrolledDateTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovedFromIntuneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSeenBySyncAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DevicesSeen = table.Column<int>(type: "int", nullable: false),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Removed = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDevices_IntuneId",
                table: "ManagedDevices",
                column: "IntuneId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDevices_SerialNumber",
                table: "ManagedDevices",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDevices_UserPrincipalName",
                table: "ManagedDevices",
                column: "UserPrincipalName");

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_Source_StartedAt",
                table: "SyncRuns",
                columns: new[] { "Source", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedDevices");

            migrationBuilder.DropTable(
                name: "SyncRuns");
        }
    }
}
