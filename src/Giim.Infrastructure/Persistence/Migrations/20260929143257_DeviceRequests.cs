using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>Scaffolded (new tables only), plus the append-only permission on DeviceRequestEvents.</remarks>
    public partial class DeviceRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "DeviceRequestNumbers",
                startValue: 1001L);

            migrationBuilder.CreateTable(
                name: "DeviceRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedByEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RecipientPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Specifications = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReasonType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NeededBy = table.Column<DateOnly>(type: "date", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TicketNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BudgetCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Supplier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PurchaseOrder = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OrderCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OrderedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpectedDelivery = table.Column<DateOnly>(type: "date", nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PurchaseNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceRequests_AssetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceRequests_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceRequests_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceRequests_People_ApproverPersonId",
                        column: x => x.ApproverPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceRequests_People_RecipientPersonId",
                        column: x => x.RecipientPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ToName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BodyText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceRequestEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceRequestEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceRequestEvents_DeviceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "DeviceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequestEvents_Actor",
                table: "DeviceRequestEvents",
                column: "Actor");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequestEvents_RequestId_OccurredAt",
                table: "DeviceRequestEvents",
                columns: new[] { "RequestId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_ApproverPersonId",
                table: "DeviceRequests",
                column: "ApproverPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_AssetId",
                table: "DeviceRequests",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_CategoryId",
                table: "DeviceRequests",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_DepartmentId",
                table: "DeviceRequests",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_Number",
                table: "DeviceRequests",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_PurchaseOrder",
                table: "DeviceRequests",
                column: "PurchaseOrder");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_RecipientPersonId",
                table: "DeviceRequests",
                column: "RecipientPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_RequestedBy",
                table: "DeviceRequests",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_Status_SubmittedAt",
                table: "DeviceRequests",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRequests_TicketNumber",
                table: "DeviceRequests",
                column: "TicketNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RequestId",
                table: "Notifications",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_NextAttemptAt",
                table: "Notifications",
                columns: new[] { "Status", "NextAttemptAt" });

            // Request history is append-only for the app, like the asset timeline (see AppendOnlyPermissions).
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID('giim_app') IS NULL CREATE ROLE giim_app;");
            migrationBuilder.Sql("DENY UPDATE, DELETE ON dbo.DeviceRequestEvents TO giim_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceRequestEvents");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "DeviceRequests");

            migrationBuilder.DropSequence(
                name: "DeviceRequestNumbers");
        }
    }
}
