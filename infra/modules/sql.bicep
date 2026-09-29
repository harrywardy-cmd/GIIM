// Azure SQL: GIIM's database. Microsoft Entra sign-in only (no SQL passwords exist), reached by the apps through
// a private endpoint. The public endpoint has no firewall rules, so it is closed; the deployment pipeline opens it
// to its own address for the few seconds it takes to run migrations, then closes it again.
// Every query and sign-in is audited to Log Analytics.

param name string
param location string
param databaseName string
@description('Entra group whose members administer the database (create users, emergency access).')
param adminGroupName string
param adminGroupObjectId string
param sku object
param maxSizeGb int
@description('Point-in-time restore window, in days (7-35).')
@minValue(7)
@maxValue(35)
param backupRetentionDays int
@description('Keep weekly, monthly and yearly backups for years (records retention).')
param longTermRetention bool
@allowed(['Local', 'Zone', 'Geo', 'GeoZone'])
param backupRedundancy string
@description('Identity allowed to open the firewall for migrations (the GitHub deployment identity).')
param deployPrincipalId string
param workspaceId string
param endpointSubnetId string
param dnsZoneId string
param tags object

var sqlServerContributor = '6d8ee4ec-f05a-4a1d-8b00-a9b17e38b437' // SQL Server Contributor

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: adminGroupName
      sid: adminGroupObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: sku
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: maxSizeGb * 1024 * 1024 * 1024
    requestedBackupStorageRedundancy: backupRedundancy
    zoneRedundant: false
  }
}

resource shortTermBackups 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01' = {
  parent: database
  name: 'default'
  properties: {
    retentionDays: backupRetentionDays
  }
}

// Weekly backups for 5 weeks, monthly for 12 months, and the first backup of each year for 7 years.
resource longTermBackups 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2023-08-01' = if (longTermRetention) {
  parent: database
  name: 'default'
  properties: {
    weeklyRetention: 'P5W'
    monthlyRetention: 'P12M'
    yearlyRetention: 'P7Y'
    weekOfYear: 1
  }
}

resource auditing 'Microsoft.Sql/servers/auditingSettings@2023-08-01' = {
  parent: server
  name: 'default'
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
  }
}

// Server audit events flow through the master database's diagnostic setting.
resource master 'Microsoft.Sql/servers/databases@2023-08-01' existing = {
  parent: server
  name: 'master'
}

resource auditToLogs 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: master
  properties: {
    workspaceId: workspaceId
    logs: [{ category: 'SQLSecurityAuditEvents', enabled: true }]
  }
  dependsOn: [auditing]
}

resource databaseDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: database
  properties: {
    workspaceId: workspaceId
    metrics: [{ category: 'Basic', enabled: true }]
  }
}

resource deployAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(server.id, deployPrincipalId, sqlServerContributor)
  scope: server
  properties: {
    principalId: deployPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sqlServerContributor)
  }
}

module endpoint 'private-endpoint.bicep' = {
  name: 'pe-${name}'
  params: {
    name: 'pe-${name}'
    location: location
    subnetId: endpointSubnetId
    serviceId: server.id
    groupId: 'sqlServer'
    dnsZoneId: dnsZoneId
    tags: tags
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
output databaseId string = database.id
