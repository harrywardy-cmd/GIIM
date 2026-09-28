using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetTimelineAndStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Assets",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TicketNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetEvents_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetEvents_Actor",
                table: "AssetEvents",
                column: "Actor");

            migrationBuilder.CreateIndex(
                name: "IX_AssetEvents_AssetId_OccurredAt",
                table: "AssetEvents",
                columns: new[] { "AssetId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetEvents_TicketNumber",
                table: "AssetEvents",
                column: "TicketNumber");

            // Hand-added: InStock was renamed ReadyToDeploy ("Available" in the brief).
            migrationBuilder.Sql("UPDATE Assets SET Status = N'ReadyToDeploy' WHERE Status = N'InStock';");

            // Hand-added: give every existing asset a starting point on its timeline, dated when it entered GIIM.
            migrationBuilder.Sql("""
                INSERT INTO AssetEvents (AssetId, OccurredAt, RecordedAt, Type, FromStatus, ToStatus, Actor, TicketNumber, Summary, Note, DetailsJson)
                SELECT a.Id, a.CreatedAt, SYSDATETIMEOFFSET(), N'Created', NULL, a.Status, N'system', NULL,
                       N'Existing record as ' + a.Status + N' (added before timeline tracking)', NULL, NULL
                FROM Assets a
                WHERE NOT EXISTS (SELECT 1 FROM AssetEvents e WHERE e.AssetId = a.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hand-added: map statuses back to the previous set. Lossy by nature: Received becomes InStock,
            // Stolen becomes Lost, Retired becomes Disposed, and Wiped-from-migration history is dropped.
            migrationBuilder.Sql("""
                UPDATE Assets SET Status = CASE Status
                    WHEN N'ReadyToDeploy' THEN N'InStock'
                    WHEN N'Received' THEN N'InStock'
                    WHEN N'Stolen' THEN N'Lost'
                    WHEN N'Retired' THEN N'Disposed'
                    ELSE Status END;
                """);

            migrationBuilder.DropTable(
                name: "AssetEvents");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Assets");
        }
    }
}
