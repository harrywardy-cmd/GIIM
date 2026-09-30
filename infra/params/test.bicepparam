// Test: the same design as production, smaller and cheaper. Can be deleted and recreated at any time.
// Fill in the values marked FILL IN before the first deployment (see infra/README.md, step 1).
using '../main.bicep'

param environmentName = 'test'

// FILL IN: the Entra group that administers the database (can be the same group as production).
param sqlAdminGroupName = 'GIIM-SQL-Admins'
param sqlAdminGroupObjectId = ''

param sqlSku = { name: 'S0', tier: 'Standard' }
param sqlMaxSizeGb = 10
param sqlBackupRetentionDays = 7
param sqlLongTermRetention = false
param sqlBackupRedundancy = 'Local'

param appServicePlanSku = { name: 'B1', tier: 'Basic', capacity: 1 }
param storageSku = 'Standard_LRS'
param keyVaultPurgeProtection = false // so a deleted test environment can be recreated straight away
param logRetentionDays = 30
param networkAddressPrefix = '10.20.4.0/22'

// FILL IN once the test app registration exists (separate from production; see docs/entra-setup.md).
param publicBaseUrl = ''
param entraClientId = ''

param alertEmails = []

// FILL IN once the mailbox exists: where request emails come from, e.g. 'giim@company.com.au' (docs/email-notifications.md).
param notificationMailbox = ''

// ServiceDesk Plus (docs/servicedesk-setup.md): switch to 'Api' once its administrator has set up the API client.
param serviceDeskMode = 'None'
param serviceDeskClientId = ''
// Never written here: deploy.ps1 -SetServiceDeskSecrets asks for them and passes them through these variables.
param serviceDeskClientSecret = readEnvironmentVariable('GIIM_SDP_CLIENT_SECRET', '')
param serviceDeskRefreshToken = readEnvironmentVariable('GIIM_SDP_REFRESH_TOKEN', '')
param serviceDeskWebhookSecret = readEnvironmentVariable('GIIM_SDP_WEBHOOK_SECRET', '')
param allowedIpRanges = []
