// Logs and telemetry: one Log Analytics workspace for everything, with Application Insights on top of it.

param name string
param location string
@description('How long logs are kept, in days.')
param retentionDays int
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionDays
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${name}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

output workspaceId string = workspace.id
output appInsightsId string = insights.id
output appInsightsConnectionString string = insights.properties.ConnectionString
