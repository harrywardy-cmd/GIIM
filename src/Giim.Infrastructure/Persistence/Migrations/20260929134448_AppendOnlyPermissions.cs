using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Giim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Hand-written. The app's own guard already refuses to edit or delete history (GiimDbContext.GuardAppendOnly);
    /// this makes the database refuse too, so the audit trail holds even against a bug or a stolen app credential.
    /// In Azure the API and workers sign in as members of giim_app (infra/sql/grant-access.sql); the deployment
    /// identity and administrators are db_owner and unaffected. Locally (sa) nothing changes.
    /// </remarks>
    public partial class AppendOnlyPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID('giim_app') IS NULL CREATE ROLE giim_app;");
            migrationBuilder.Sql("ALTER ROLE db_datareader ADD MEMBER giim_app;");
            migrationBuilder.Sql("ALTER ROLE db_datawriter ADD MEMBER giim_app;");
            migrationBuilder.Sql("DENY UPDATE, DELETE ON dbo.AssetEvents TO giim_app;");
            migrationBuilder.Sql("DENY UPDATE, DELETE ON dbo.AuditEntries TO giim_app;");
            migrationBuilder.Sql("DENY UPDATE, DELETE ON dbo.StockMovements TO giim_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The role stays (app users may belong to it); only the extra protection is lifted.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON dbo.AssetEvents TO giim_app;");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON dbo.AuditEntries TO giim_app;");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON dbo.StockMovements TO giim_app;");
        }
    }
}
