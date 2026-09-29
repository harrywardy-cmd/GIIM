// Key Vault: GIIM's secrets (Okta client secret; later ServiceDesk Plus) and the key that protects sign-in cookie
// keys. Reachable only through the private network; access is by Azure role, never by access policy or password.

param name string
param location string
param workspaceId string
param endpointSubnetId string
param dnsZoneId string
@description('Identities that may read secrets (the apps).')
param secretReaderPrincipalIds array
@description('Identities that may use the cookie-protection key (the API).')
param keyUserPrincipalIds array
@description('Stops anyone permanently deleting the vault or its contents for 90 days. Cannot be turned off once on.')
param purgeProtection bool
@secure()
@description('Okta client secret. Leave empty to keep the value already in the vault.')
param oktaClientSecret string
param tags object

var roles = {
  secretsUser: '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
  cryptoUser: '12338af0-0e69-4776-bea7-57ae8d297424' // Key Vault Crypto User
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: purgeProtection ? true : null
    publicNetworkAccess: 'Disabled'
    networkAcls: { defaultAction: 'Deny', bypass: 'None' }
  }
}

// Wraps the ASP.NET Core data-protection keys stored in Blob Storage (see WebHosting.StoreKeys).
resource cookieKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: ['wrapKey', 'unwrapKey']
  }
}

// Set through the deployment (not the vault's data plane, which is private) and only when a value is given.
resource oktaSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(oktaClientSecret)) {
  parent: vault
  name: 'okta-client-secret'
  properties: {
    value: oktaClientSecret
    contentType: 'Okta OIDC client secret for GIIM'
  }
}

resource secretReaders 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in secretReaderPrincipalIds: {
  name: guid(vault.id, principalId, roles.secretsUser)
  scope: vault
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.secretsUser)
  }
}]

resource keyUsers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in keyUserPrincipalIds: {
  name: guid(cookieKey.id, principalId, roles.cryptoUser)
  scope: cookieKey
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cryptoUser)
  }
}]

module endpoint 'private-endpoint.bicep' = {
  name: 'pe-${name}'
  params: {
    name: 'pe-${name}'
    location: location
    subnetId: endpointSubnetId
    serviceId: vault.id
    groupId: 'vault'
    dnsZoneId: dnsZoneId
    tags: tags
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: vault
  properties: {
    workspaceId: workspaceId
    logs: [{ categoryGroup: 'audit', enabled: true }]
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
output cookieKeyUri string = '${vault.properties.vaultUri}keys/${cookieKey.name}'
output oktaSecretUri string = '${vault.properties.vaultUri}secrets/okta-client-secret'
