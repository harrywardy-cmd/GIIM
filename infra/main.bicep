// GIIM in Azure: everything one environment (test or prod) needs, in one resource group.
// Deploy with infra/deploy.ps1; see infra/README.md for the full runbook.

targetScope = 'resourceGroup'

@allowed(['test', 'prod'])
param environmentName string

param location string = resourceGroup().location

@description('GitHub repository allowed to deploy (owner/name).')
param githubRepository string = 'harrywardy-cmd/GIIM'

// ---- Database ---------------------------------------------------------------------------------------------------

@description('Display name of the Entra group that administers the database, e.g. GIIM-SQL-Admins.')
@minLength(1)
param sqlAdminGroupName string

@description('Object ID of that group (Entra admin centre > Groups > the group > Object ID).')
@minLength(36)
@maxLength(36)
param sqlAdminGroupObjectId string

@description('Database size. DTU sizes (Basic, S0-S12) suit GIIM well; vCore sizes (e.g. GP_Gen5_2) also work.')
param sqlSku object = { name: 'S2', tier: 'Standard' }

param sqlMaxSizeGb int = 50
param sqlBackupRetentionDays int = 14
param sqlLongTermRetention bool = true

@allowed(['Local', 'Zone', 'Geo', 'GeoZone'])
param sqlBackupRedundancy string = 'Geo'

// ---- Hosting ----------------------------------------------------------------------------------------------------

@description('App Service plan size and instance count.')
param appServicePlanSku object = { name: 'P0v3', tier: 'PremiumV3', capacity: 1 }

@description('The address staff use, e.g. https://giim.company.com.au. Empty: the azurewebsites.net address.')
param publicBaseUrl string = ''

@description('If not empty, only these IP ranges (CIDR) can open GIIM, e.g. office and VPN addresses.')
param allowedIpRanges array = []

@description('Private network address space (a /22); must not overlap networks it may be connected to later.')
param networkAddressPrefix string = '10.20.0.0/22'

// ---- Sign-in, integrations, operations ----------------------------------------------------------------------------

@description('Entra tenant (directory) staff sign in with. Normally the tenant this subscription belongs to.')
param entraTenantId string = subscription().tenantId

@description('Application (client) ID of the GIIM app registration in Entra. Empty until it exists (docs/entra-setup.md).')
param entraClientId string = ''

param intuneSyncInterval string = '04:00:00'

// Staff directory from Entra ID (docs/staff-directory.md). Every member account with an employee ID is read by default.
@description('Extra filter on Entra accounts, e.g. companyName eq \'Contoso\'. Empty: every member account with an employee ID.')
param directoryFilter string = ''

@description('On-premises extension attribute holding the starter track (Full or Light), e.g. extensionAttribute5. Empty: tracks are set in GIIM.')
param directoryTrackAttribute string = ''

@description('Read leave dates from Entra. Needs User-LifeCycleInfo.Read.All: run Grant-GraphAccess.ps1 with -IncludeLeaveDates first.')
param directoryReadLeaveDates bool = false

@description('Mailbox GIIM sends emails from, e.g. giim@company.com.au. Empty: emails wait in the outbox (see docs/email-notifications.md).')
param notificationMailbox string = ''

@description('Who gets the daily IT digest and weekly warranty list, e.g. it-team@company.com.au (several: separate with ;).')
param itTeamEmails string = ''

@allowed(['None', 'Api'])
@description('Connect to ServiceDesk Plus (docs/servicedesk-setup.md). None until its administrator has set up the API client and trigger.')
param serviceDeskMode string = 'None'

@description('ServiceDesk Plus (Zoho) client ID. Not secret.')
param serviceDeskClientId string = ''

@secure()
@description('Pass only when setting or changing it (deploy.ps1 -SetServiceDeskSecrets).')
param serviceDeskClientSecret string = ''

@secure()
@description('Pass only when setting or changing it (deploy.ps1 -SetServiceDeskSecrets).')
param serviceDeskRefreshToken string = ''

@secure()
@description('Pass only when setting or changing it (deploy.ps1 -SetServiceDeskSecrets).')
param serviceDeskWebhookSecret string = ''

@description('Who gets alert emails.')
param alertEmails array = []

param logRetentionDays int = 90

@allowed(['Standard_LRS', 'Standard_ZRS', 'Standard_GRS', 'Standard_GZRS'])
param storageSku string = 'Standard_ZRS'

@description('Protect Key Vault contents from permanent deletion for 90 days. Recommended for prod; cannot be undone.')
param keyVaultPurgeProtection bool = true

param tags object = {}

// ---- Names ------------------------------------------------------------------------------------------------------

var name = 'giim-${environmentName}'
var suffix = take(uniqueString(resourceGroup().id), 6) // globally unique names stay stable for this resource group
var allTags = union({ application: 'GIIM', environment: environmentName }, tags)

var apiName = 'app-${name}-${suffix}'
var workersName = 'app-${name}-workers-${suffix}'
var effectivePublicUrl = empty(publicBaseUrl) ? 'https://${apiName}.azurewebsites.net' : publicBaseUrl

// ---- Resources --------------------------------------------------------------------------------------------------

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { name: name, location: location, retentionDays: logRetentionDays, tags: allTags }
}

module identities 'modules/identities.bicep' = {
  name: 'identities'
  params: {
    name: name
    location: location
    githubRepository: githubRepository
    githubEnvironment: environmentName
    tags: allTags
  }
}

module network 'modules/network.bicep' = {
  name: 'network'
  params: { name: name, location: location, addressPrefix: networkAddressPrefix, tags: allTags }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: 'kv-${name}-${suffix}'
    location: location
    workspaceId: monitoring.outputs.workspaceId
    endpointSubnetId: network.outputs.endpointSubnetId
    dnsZoneId: network.outputs.dnsZoneIds.vault
    secretReaderPrincipalIds: [identities.outputs.api.principalId, identities.outputs.workers.principalId]
    keyUserPrincipalIds: [identities.outputs.api.principalId]
    purgeProtection: keyVaultPurgeProtection
    serviceDeskClientSecret: serviceDeskClientSecret
    serviceDeskRefreshToken: serviceDeskRefreshToken
    serviceDeskWebhookSecret: serviceDeskWebhookSecret
    tags: allTags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: 'stgiim${environmentName}${suffix}'
    location: location
    skuName: storageSku
    workspaceId: monitoring.outputs.workspaceId
    endpointSubnetId: network.outputs.endpointSubnetId
    dnsZoneId: network.outputs.dnsZoneIds.blob
    dataProtectionPrincipalIds: [identities.outputs.api.principalId]
    attachmentsPrincipalIds: [identities.outputs.api.principalId]
    tags: allTags
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    name: 'sql-${name}-${suffix}'
    location: location
    databaseName: 'giim'
    adminGroupName: sqlAdminGroupName
    adminGroupObjectId: sqlAdminGroupObjectId
    sku: sqlSku
    maxSizeGb: sqlMaxSizeGb
    backupRetentionDays: sqlBackupRetentionDays
    longTermRetention: sqlLongTermRetention
    backupRedundancy: sqlBackupRedundancy
    deployPrincipalId: identities.outputs.deploy.principalId
    workspaceId: monitoring.outputs.workspaceId
    endpointSubnetId: network.outputs.endpointSubnetId
    dnsZoneId: network.outputs.dnsZoneIds.sql
    tags: allTags
  }
}

module apps 'modules/apps.bicep' = {
  name: 'apps'
  params: {
    name: name
    location: location
    apiName: apiName
    workersName: workersName
    planSku: appServicePlanSku
    apiIdentity: identities.outputs.api
    workersIdentity: identities.outputs.workers
    deployPrincipalId: identities.outputs.deploy.principalId
    appSubnetId: network.outputs.appSubnetId
    workspaceId: monitoring.outputs.workspaceId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    sqlServerFqdn: sql.outputs.serverFqdn
    databaseName: sql.outputs.databaseName
    dataProtectionBlobUri: storage.outputs.dataProtectionBlobUri
    dataProtectionKeyUri: keyVault.outputs.cookieKeyUri
    attachmentsContainerUri: storage.outputs.attachmentsContainerUri
    publicBaseUrl: effectivePublicUrl
    entraTenantId: entraTenantId
    entraClientId: entraClientId
    allowedIpRanges: allowedIpRanges
    intuneSyncInterval: intuneSyncInterval
    directoryFilter: directoryFilter
    directoryTrackAttribute: directoryTrackAttribute
    directoryReadLeaveDates: directoryReadLeaveDates
    notificationMailbox: notificationMailbox
    itTeamEmails: itTeamEmails
    serviceDeskMode: serviceDeskMode
    serviceDeskClientId: serviceDeskClientId
    keyVaultUri: keyVault.outputs.uri
    tags: allTags
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    name: name
    location: location
    emails: alertEmails
    apiId: apps.outputs.apiId
    workersId: apps.outputs.workersId
    databaseId: sql.outputs.databaseId
    appInsightsId: monitoring.outputs.appInsightsId
    dtuBased: contains(['Basic', 'Standard', 'Premium'], sqlSku.tier)
    tags: allTags
  }
}

// ---- Outputs: used by the scripts in infra/scripts and as GitHub environment variables ---------------------------

output publicUrl string = effectivePublicUrl
output signInRedirectUri string = '${effectivePublicUrl}/signin-oidc'
output signOutRedirectUri string = '${effectivePublicUrl}/signout-callback-oidc'
output myAppsHomePageUrl string = '${effectivePublicUrl}/auth/login'
output serviceDeskWebhookUrl string = '${effectivePublicUrl}/integrations/servicedesk/webhook'
output outboundIpAddress string = network.outputs.outboundIpAddress

output apiAppName string = apps.outputs.apiName
output workersAppName string = apps.outputs.workersName
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.serverFqdn
output databaseName string = sql.outputs.databaseName
output keyVaultName string = keyVault.outputs.name

output apiIdentityName string = identities.outputs.api.name
output apiIdentityPrincipalId string = identities.outputs.api.principalId
output apiIdentityClientId string = identities.outputs.api.clientId
output workersIdentityName string = identities.outputs.workers.name
output workersIdentityPrincipalId string = identities.outputs.workers.principalId
output workersIdentityClientId string = identities.outputs.workers.clientId
output deployIdentityName string = identities.outputs.deploy.name
output deployClientId string = identities.outputs.deploy.clientId
output tenantId string = subscription().tenantId
output subscriptionId string = subscription().subscriptionId
output resourceGroupName string = resourceGroup().name
