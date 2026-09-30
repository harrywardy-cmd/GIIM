using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// GIIM uses Microsoft Entra ID only. Scaffolded, plus renaming the "OktaGroup" profile item type. People.OktaUserId
    /// was never filled in (Entra object IDs go in People.EntraObjectId), so dropping it loses nothing.
    /// </remarks>
    public partial class RemoveOkta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_OktaUserId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "OktaUserId",
                table: "People");

            migrationBuilder.RenameColumn(
                name: "OktaGroupName",
                table: "Applications",
                newName: "AccessGroupName");

            migrationBuilder.Sql("UPDATE ProfileItems SET Type = 'SecurityGroup' WHERE Type = 'OktaGroup';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE ProfileItems SET Type = 'OktaGroup' WHERE Type = 'SecurityGroup';");

            migrationBuilder.RenameColumn(
                name: "AccessGroupName",
                table: "Applications",
                newName: "OktaGroupName");

            migrationBuilder.AddColumn<string>(
                name: "OktaUserId",
                table: "People",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_OktaUserId",
                table: "People",
                column: "OktaUserId");
        }
    }
}
