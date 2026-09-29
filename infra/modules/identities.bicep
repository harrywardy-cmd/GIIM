// Managed identities: how GIIM signs in to other services without any stored password.
//   api     - the web app: SQL, Key Vault (Okta secret, cookie key), Blob Storage, Microsoft Graph
//   workers - the background jobs: SQL, Key Vault, Microsoft Graph
//   deploy  - GitHub Actions: deploys code and runs database migrations. It trusts only this repository's
//             GitHub environment of the same name, so protection rules on that environment (required reviewers)
//             control who can deploy.

param name string
param location string
param githubRepository string
param githubEnvironment string
param tags object

resource api 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}-api'
  location: location
  tags: tags
}

resource workers 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}-workers'
  location: location
  tags: tags
}

resource deploy 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}-deploy'
  location: location
  tags: tags

  resource github 'federatedIdentityCredentials' = {
    name: 'github-${githubEnvironment}'
    properties: {
      issuer: 'https://token.actions.githubusercontent.com'
      subject: 'repo:${githubRepository}:environment:${githubEnvironment}'
      audiences: ['api://AzureADTokenExchange']
    }
  }
}

output api object = { id: api.id, name: api.name, clientId: api.properties.clientId, principalId: api.properties.principalId }
output workers object = { id: workers.id, name: workers.name, clientId: workers.properties.clientId, principalId: workers.properties.principalId }
output deploy object = { id: deploy.id, name: deploy.name, clientId: deploy.properties.clientId, principalId: deploy.properties.principalId }
