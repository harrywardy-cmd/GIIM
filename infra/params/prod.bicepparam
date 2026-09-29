// Production. Fill in the values marked FILL IN before the first deployment (see infra/README.md, step 1).
// Nothing in this file is secret; the Okta client secret is passed separately (deploy.ps1 -SetOktaSecret).
using '../main.bicep'

param environmentName = 'prod'

// FILL IN: the Entra group that administers the database.
param sqlAdminGroupName = 'GIIM-SQL-Admins'
param sqlAdminGroupObjectId = ''

// S2 = 50 DTUs: comfortable for 1,500 people and ~3,000-10,000 assets. Scale up later without downtime.
param sqlSku = { name: 'S2', tier: 'Standard' }
param sqlMaxSizeGb = 50
param sqlBackupRetentionDays = 14
param sqlLongTermRetention = true
param sqlBackupRedundancy = 'Geo' // backups copied to Australia Southeast

param appServicePlanSku = { name: 'P0v3', tier: 'PremiumV3', capacity: 1 }
param storageSku = 'Standard_ZRS'
param keyVaultPurgeProtection = true
param logRetentionDays = 90

// FILL IN once known. Until then GIIM runs at its azurewebsites.net address and sign-in won't work.
param publicBaseUrl = ''
param oktaAuthority = ''
param oktaClientId = ''
// Never written here: deploy.ps1 -SetOktaSecret asks for it and passes it through this environment variable.
param oktaClientSecret = readEnvironmentVariable('GIIM_OKTA_CLIENT_SECRET', '')

// FILL IN: who gets alert emails, e.g. ['it-alerts@company.com.au'].
param alertEmails = []

// Optional: only allow these addresses (office, VPN) to open GIIM, e.g. ['203.0.113.0/24'].
param allowedIpRanges = []
