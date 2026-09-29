// Blob Storage: sign-in cookie keys now; asset photos and attachments later. Private network only, no account
// keys or anonymous access: every read and write is by a managed identity with an Azure role. Deleted or
// overwritten files can be recovered for 30 days.

param name string
param location string
@allowed(['Standard_LRS', 'Standard_ZRS', 'Standard_GRS', 'Standard_GZRS'])
param skuName string
param workspaceId string
param endpointSubnetId string
param dnsZoneId string
@description('Identities that read and write the cookie keys (the API).')
param dataProtectionPrincipalIds array
param tags object

var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: skuName }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Disabled'
    networkAcls: { defaultAction: 'Deny', bypass: 'None' }
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: { enabled: true, days: 30 }
    containerDeleteRetentionPolicy: { enabled: true, days: 30 }
  }
}

resource dataProtection 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'dataprotection'
  properties: { publicAccess: 'None' }
}

resource attachments 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'attachments'
  properties: { publicAccess: 'None' }
}

resource dataProtectionAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in dataProtectionPrincipalIds: {
  name: guid(dataProtection.id, principalId, blobDataContributor)
  scope: dataProtection
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
  }
}]

module endpoint 'private-endpoint.bicep' = {
  name: 'pe-${name}-blob'
  params: {
    name: 'pe-${name}-blob'
    location: location
    subnetId: endpointSubnetId
    serviceId: account.id
    groupId: 'blob'
    dnsZoneId: dnsZoneId
    tags: tags
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: blobs
  properties: {
    workspaceId: workspaceId
    logs: [{ categoryGroup: 'allLogs', enabled: true }]
  }
}

output name string = account.name
output dataProtectionBlobUri string = '${account.properties.primaryEndpoints.blob}${dataProtection.name}/keys.xml'
output attachmentsContainerUri string = '${account.properties.primaryEndpoints.blob}${attachments.name}'
