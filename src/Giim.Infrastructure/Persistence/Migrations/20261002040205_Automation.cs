using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Automation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Step",
                table: "ChecklistTasks",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StepTarget",
                table: "ChecklistTasks",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentCheckIns",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Directory = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DryRun = table.Column<bool>(type: "bit", nullable: false),
                    LastJobAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentCheckIns", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "AutomationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Runner = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DependsOnJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NotBefore = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DryRun = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    ClaimedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Log = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationJobs_AutomationJobs_DependsOnJobId",
                        column: x => x.DependsOnJobId,
                        principalTable: "AutomationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationJobs_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationJobs_ChecklistTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "ChecklistTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationJobs_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationJobs_CaseId",
                table: "AutomationJobs",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationJobs_DependsOnJobId",
                table: "AutomationJobs",
                column: "DependsOnJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationJobs_PersonId",
                table: "AutomationJobs",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationJobs_Runner_Status_NotBefore",
                table: "AutomationJobs",
                columns: new[] { "Runner", "Status", "NotBefore" });

            migrationBuilder.CreateIndex(
                name: "UX_AutomationJobs_OneActivePerTask",
                table: "AutomationJobs",
                column: "TaskId",
                unique: true,
                filter: "[Status] IN ('Queued', 'Running')");

            // Starter checklists created before this: mark the steps automation can do, as the generator now does.
            migrationBuilder.Sql("""
                UPDATE t SET Step = CASE
                    WHEN t.Title = 'Create AD account' THEN 'CreateAccount'
                    WHEN t.Title LIKE 'Wait for Entra Connect%' THEN 'WaitForCloudSync'
                    WHEN t.Title LIKE 'Enable remote mailbox%' THEN 'EnableRemoteMailbox'
                    WHEN t.Title = 'Enable the account on the start date' THEN 'EnableAccount'
                    WHEN t.Title = 'Send welcome email to manager' THEN 'SendWelcomeEmail'
                END
                FROM ChecklistTasks t JOIN Cases c ON c.Id = t.CaseId
                WHERE c.Type = 'Onboarding' AND t.Kind = 'Automated' AND t.Source = 'None';

                UPDATE t SET Step = 'AddToGroup', StepTarget = LTRIM(RTRIM(i.GroupName))
                FROM ChecklistTasks t
                JOIN Cases c ON c.Id = t.CaseId
                JOIN ProfileItems i ON i.Id = t.SourceId
                WHERE c.Type = 'Onboarding' AND t.Kind = 'Automated' AND t.Source = 'ProfileItem'
                  AND i.GroupName IS NOT NULL AND LTRIM(RTRIM(i.GroupName)) <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentCheckIns");

            migrationBuilder.DropTable(
                name: "AutomationJobs");

            migrationBuilder.DropColumn(
                name: "Step",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "StepTarget",
                table: "ChecklistTasks");
        }
    }
}
