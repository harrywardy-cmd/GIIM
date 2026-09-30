using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Scaffolded. The case and profile tables existed from the start but were never used, so they are empty when this
    /// runs and the shorter text columns lose nothing. New task columns default to Source = None.
    /// </remarks>
    public partial class OnboardingOffboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cases_Status",
                table: "Cases");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "RoleProfiles",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "JobTitle",
                table: "RoleProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GroupName",
                table: "ProfileItems",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ProfileItems",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "ChecklistTasks",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ServiceDeskTaskId",
                table: "ChecklistTasks",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "ChecklistTasks",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedTo",
                table: "ChecklistTasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApprovedBy",
                table: "ChecklistTasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "ChecklistTasks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "ChecklistTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletedBy",
                table: "ChecklistTasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceRequestId",
                table: "ChecklistTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ChecklistTasks",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AlterColumn<string>(
                name: "ServiceDeskRequestId",
                table: "Cases",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Cases",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "Cases",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "Cases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Cases",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Cases",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoleProfileId",
                table: "Cases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Cases",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleProfiles_DepartmentId_JobTitle",
                table: "RoleProfiles",
                columns: new[] { "DepartmentId", "JobTitle" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileItems_CategoryId",
                table: "ProfileItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileItems_StockItemId",
                table: "ProfileItems",
                column: "StockItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTasks_CategoryId",
                table: "ChecklistTasks",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTasks_DeviceRequestId",
                table: "ChecklistTasks",
                column: "DeviceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTasks_Source_SourceId",
                table: "ChecklistTasks",
                columns: new[] { "Source", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_PersonId",
                table: "Cases",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_RoleProfileId",
                table: "Cases",
                column: "RoleProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_Type_Status_DueDate",
                table: "Cases",
                columns: new[] { "Type", "Status", "DueDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Cases_People_PersonId",
                table: "Cases",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Cases_RoleProfiles_RoleProfileId",
                table: "Cases",
                column: "RoleProfileId",
                principalTable: "RoleProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChecklistTasks_AssetCategories_CategoryId",
                table: "ChecklistTasks",
                column: "CategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChecklistTasks_DeviceRequests_DeviceRequestId",
                table: "ChecklistTasks",
                column: "DeviceRequestId",
                principalTable: "DeviceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProfileItems_AssetCategories_CategoryId",
                table: "ProfileItems",
                column: "CategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProfileItems_StockItems_StockItemId",
                table: "ProfileItems",
                column: "StockItemId",
                principalTable: "StockItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleProfiles_Departments_DepartmentId",
                table: "RoleProfiles",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cases_People_PersonId",
                table: "Cases");

            migrationBuilder.DropForeignKey(
                name: "FK_Cases_RoleProfiles_RoleProfileId",
                table: "Cases");

            migrationBuilder.DropForeignKey(
                name: "FK_ChecklistTasks_AssetCategories_CategoryId",
                table: "ChecklistTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ChecklistTasks_DeviceRequests_DeviceRequestId",
                table: "ChecklistTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ProfileItems_AssetCategories_CategoryId",
                table: "ProfileItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ProfileItems_StockItems_StockItemId",
                table: "ProfileItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleProfiles_Departments_DepartmentId",
                table: "RoleProfiles");

            migrationBuilder.DropIndex(
                name: "IX_RoleProfiles_DepartmentId_JobTitle",
                table: "RoleProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProfileItems_CategoryId",
                table: "ProfileItems");

            migrationBuilder.DropIndex(
                name: "IX_ProfileItems_StockItemId",
                table: "ProfileItems");

            migrationBuilder.DropIndex(
                name: "IX_ChecklistTasks_CategoryId",
                table: "ChecklistTasks");

            migrationBuilder.DropIndex(
                name: "IX_ChecklistTasks_DeviceRequestId",
                table: "ChecklistTasks");

            migrationBuilder.DropIndex(
                name: "IX_ChecklistTasks_Source_SourceId",
                table: "ChecklistTasks");

            migrationBuilder.DropIndex(
                name: "IX_Cases_PersonId",
                table: "Cases");

            migrationBuilder.DropIndex(
                name: "IX_Cases_RoleProfileId",
                table: "Cases");

            migrationBuilder.DropIndex(
                name: "IX_Cases_Type_Status_DueDate",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "CompletedBy",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "DeviceRequestId",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "RoleProfileId",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Cases");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "RoleProfiles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "JobTitle",
                table: "RoleProfiles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GroupName",
                table: "ProfileItems",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ProfileItems",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "ChecklistTasks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "ServiceDeskTaskId",
                table: "ChecklistTasks",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "ChecklistTasks",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedTo",
                table: "ChecklistTasks",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApprovedBy",
                table: "ChecklistTasks",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ServiceDeskRequestId",
                table: "Cases",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cases_Status",
                table: "Cases",
                column: "Status");
        }
    }
}
