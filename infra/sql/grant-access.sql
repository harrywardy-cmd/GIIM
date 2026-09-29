-- Gives GIIM's managed identities access to its database. Safe to run again.
-- Run with infra/scripts/Grant-SqlAccess.ps1, by a member of the SQL admin group (a person, not a service:
-- creating users for Entra identities looks them up in the directory as the person running this).

SET NOCOUNT ON;

-- giim_app: read and write everything, but never edit or delete history. The DENY rules on the history tables are
-- added by the AppendOnlyPermissions migration, once those tables exist.
IF DATABASE_PRINCIPAL_ID('giim_app') IS NULL CREATE ROLE giim_app;
ALTER ROLE db_datareader ADD MEMBER giim_app;
ALTER ROLE db_datawriter ADD MEMBER giim_app;

-- The web app and the background workers.
IF DATABASE_PRINCIPAL_ID('$(ApiIdentity)') IS NULL CREATE USER [$(ApiIdentity)] FROM EXTERNAL PROVIDER;
ALTER ROLE giim_app ADD MEMBER [$(ApiIdentity)];

IF DATABASE_PRINCIPAL_ID('$(WorkersIdentity)') IS NULL CREATE USER [$(WorkersIdentity)] FROM EXTERNAL PROVIDER;
ALTER ROLE giim_app ADD MEMBER [$(WorkersIdentity)];

-- The GitHub deployment identity runs the migrations, so it owns the schema.
IF DATABASE_PRINCIPAL_ID('$(DeployIdentity)') IS NULL CREATE USER [$(DeployIdentity)] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [$(DeployIdentity)];

SELECT p.name AS [user], r.name AS [role]
FROM sys.database_role_members m
JOIN sys.database_principals p ON p.principal_id = m.member_principal_id
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
WHERE p.name IN ('$(ApiIdentity)', '$(WorkersIdentity)', '$(DeployIdentity)')
ORDER BY p.name;
