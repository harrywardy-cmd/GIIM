// Production. Fill in the values marked FILL IN before the first deployment (see infra/README.md, step 1).
// Nothing in this file is secret: GIIM signs in to Entra with its managed identity, so there is no client secret.
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

// FILL IN once known. Until then GIIM runs at its azurewebsites.net address.
param publicBaseUrl = ''

// FILL IN once the GIIM app registration exists (docs/entra-setup.md). Until then the sign-in page says it isn't set up.
param entraClientId = ''

// FILL IN: who gets alert emails, e.g. ['it-alerts@company.com.au'].
param alertEmails = []

// FILL IN once the mailbox exists: where request emails come from, e.g. 'giim@company.com.au' (docs/email-notifications.md).
param notificationMailbox = ''

// FILL IN: who gets the daily IT digest and weekly warranty list, e.g. 'it-team@company.com.au' (several: separate with ;).
param itTeamEmails = ''

// ServiceDesk Plus (docs/servicedesk-setup.md): switch to 'Api' once its administrator has set up the API client.
param serviceDeskMode = 'None'
param serviceDeskClientId = ''
// Never written here: deploy.ps1 -SetServiceDeskSecrets asks for them and passes them through these variables.
param serviceDeskClientSecret = readEnvironmentVariable('GIIM_SDP_CLIENT_SECRET', '')
param serviceDeskRefreshToken = readEnvironmentVariable('GIIM_SDP_REFRESH_TOKEN', '')
param serviceDeskWebhookSecret = readEnvironmentVariable('GIIM_SDP_WEBHOOK_SECRET', '')

// Optional: only allow these addresses (office, VPN) to open GIIM, e.g. ['203.0.113.0/24'].
param allowedIpRanges = []
