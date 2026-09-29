// App Service: one Linux plan running two apps.
//   API     - the web UI and API that staff use. Scales out with the plan.
//   Workers - background jobs (Intune sync). Always exactly one instance, not reachable from the internet.
// Both run as their own managed identity, send outbound traffic through the private network, and have
// password-based deployment (FTP / basic auth) switched off.

param name string
param location string
param apiName string
param workersName string
param planSku object
param apiIdentity object
param workersIdentity object
param deployPrincipalId string
param appSubnetId string
param workspaceId string
param appInsightsConnectionString string
param sqlServerFqdn string
param databaseName string
param dataProtectionBlobUri string
param dataProtectionKeyUri string
param oktaSecretUri string
param publicBaseUrl string
param oktaAuthority string
param oktaClientId string
@description('If not empty, only these IP ranges (CIDR) can open GIIM, e.g. office and VPN addresses.')
param allowedIpRanges array
param intuneSyncInterval string
param tags object

var websiteContributor = 'de139f84-1756-47ae-9be6-808fbbe84772' // Website Contributor

// Settings both apps share. Sign-in to Azure services uses the app's own managed identity only.
func sqlConnection(server string, db string, clientId string) string =>
  'Server=tcp:${server},1433;Database=${db};Authentication=Active Directory Managed Identity;User Id=${clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30'

var commonSettings = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
  AZURE_TOKEN_CREDENTIALS: 'ManagedIdentityCredential'
  Intune__Source: 'Graph'
  SCM_DO_BUILD_DURING_DEPLOYMENT: 'false'
  WEBSITE_RUN_FROM_PACKAGE: '1'
}

var apiSettings = union(commonSettings, {
  AZURE_CLIENT_ID: apiIdentity.clientId
  ConnectionStrings__Giim: sqlConnection(sqlServerFqdn, databaseName, apiIdentity.clientId)
  ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'true' // App Service ends HTTPS in front of the app; trust its headers
  Giim__PublicBaseUrl: publicBaseUrl
  DataProtection__BlobUri: dataProtectionBlobUri
  DataProtection__KeyUri: dataProtectionKeyUri
  Auth__Mode: 'Okta'
  Auth__Okta__Authority: oktaAuthority
  Auth__Okta__ClientId: oktaClientId
  Auth__Okta__ClientSecret: '@Microsoft.KeyVault(SecretUri=${oktaSecretUri})'
  Intune__SyncInterval: '00:00:00' // the API only syncs on request; the workers run the schedule
})

var workersSettings = union(commonSettings, {
  AZURE_CLIENT_ID: workersIdentity.clientId
  ConnectionStrings__Giim: sqlConnection(sqlServerFqdn, databaseName, workersIdentity.clientId)
  Intune__SyncInterval: intuneSyncInterval
})

var apiAppSettings = [for setting in items(apiSettings): { name: setting.key, value: setting.value }]
var workersAppSettings = [for setting in items(workersSettings): { name: setting.key, value: setting.value }]

var allowRules = [for (range, i) in allowedIpRanges: {
  name: 'allowed-${i}'
  ipAddress: range
  action: 'Allow'
  priority: 100 + i
}]

var siteBase = {
  linuxFxVersion: 'DOTNETCORE|10.0'
  alwaysOn: true
  ftpsState: 'Disabled'
  minTlsVersion: '1.2'
  scmMinTlsVersion: '1.2'
  http20Enabled: true
  healthCheckPath: '/health/live'
  vnetRouteAllEnabled: true
  // Deployments go through the separate SCM site with Entra sign-in; basic auth is off (see below).
  scmIpSecurityRestrictionsUseMain: false
  scmIpSecurityRestrictionsDefaultAction: 'Allow'
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${name}'
  location: location
  tags: tags
  kind: 'linux'
  sku: planSku
  properties: {
    reserved: true // Linux
    perSiteScaling: true // lets the workers stay at one instance when the API scales out
  }
}

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: apiName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    virtualNetworkSubnetId: appSubnetId
    keyVaultReferenceIdentity: apiIdentity.id
    siteConfig: union(siteBase, {
      appCommandLine: 'dotnet Giim.Api.dll'
      ipSecurityRestrictionsDefaultAction: empty(allowedIpRanges) ? 'Allow' : 'Deny'
      ipSecurityRestrictions: allowRules
      appSettings: apiAppSettings
    })
  }
}

resource workers 'Microsoft.Web/sites@2024-04-01' = {
  name: workersName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${workersIdentity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    virtualNetworkSubnetId: appSubnetId
    keyVaultReferenceIdentity: workersIdentity.id
    siteConfig: union(siteBase, {
      appCommandLine: 'dotnet Giim.Workers.dll'
      numberOfWorkers: 1
      ipSecurityRestrictionsDefaultAction: 'Deny' // nothing needs to call the workers
      appSettings: workersAppSettings
    })
  }
}

// No FTP or username/password deployments; the pipeline deploys with its Entra identity.
resource apiNoFtp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: api
  name: 'ftp'
  properties: { allow: false }
}

resource apiNoBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: api
  name: 'scm'
  properties: { allow: false }
}

resource workersNoFtp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: workers
  name: 'ftp'
  properties: { allow: false }
}

resource workersNoBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: workers
  name: 'scm'
  properties: { allow: false }
}

resource deployApi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(api.id, deployPrincipalId, websiteContributor)
  scope: api
  properties: {
    principalId: deployPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', websiteContributor)
  }
}

resource deployWorkers 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(workers.id, deployPrincipalId, websiteContributor)
  scope: workers
  properties: {
    principalId: deployPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', websiteContributor)
  }
}

resource apiDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: api
  properties: {
    workspaceId: workspaceId
    logs: [{ categoryGroup: 'allLogs', enabled: true }]
  }
}

resource workersDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: workers
  properties: {
    workspaceId: workspaceId
    logs: [{ categoryGroup: 'allLogs', enabled: true }]
  }
}

output apiName string = api.name
output apiId string = api.id
output apiHostName string = api.properties.defaultHostName
output workersName string = workers.name
output workersId string = workers.id
