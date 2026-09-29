// Alerts, emailed to the IT team: the site is failing, an app is unhealthy, the database is nearly full or
// overloaded, or the Intune sync has failed.

param name string
param location string
param emails array
param apiId string
param workersId string
param databaseId string
param appInsightsId string
@description('True for DTU sizes (Basic, S0-S12, P1-P15); false for vCore sizes.')
param dtuBased bool
param tags object

resource team 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${name}'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: take('GIIM ${name}', 12)
    enabled: true
    emailReceivers: [for (email, i) in emails: {
      name: 'email-${i}'
      emailAddress: email
      useCommonAlertSchema: true
    }]
  }
}

var metricAlerts = [
  {
    key: 'api-errors'
    description: 'GIIM is returning server errors (more than 10 in 5 minutes).'
    scope: apiId
    namespace: 'Microsoft.Web/sites'
    metric: 'Http5xx'
    aggregation: 'Total'
    operator: 'GreaterThan'
    threshold: 10
    window: 'PT5M'
    severity: 1
  }
  {
    key: 'api-health'
    description: 'The GIIM website is failing its health check.'
    scope: apiId
    namespace: 'Microsoft.Web/sites'
    metric: 'HealthCheckStatus'
    aggregation: 'Average'
    operator: 'LessThan'
    threshold: 100
    window: 'PT5M'
    severity: 1
  }
  {
    key: 'workers-health'
    description: 'The GIIM background workers are failing their health check.'
    scope: workersId
    namespace: 'Microsoft.Web/sites'
    metric: 'HealthCheckStatus'
    aggregation: 'Average'
    operator: 'LessThan'
    threshold: 100
    window: 'PT15M'
    severity: 2
  }
  {
    key: 'db-storage'
    description: 'The GIIM database is over 80% full.'
    scope: databaseId
    namespace: 'Microsoft.Sql/servers/databases'
    metric: 'storage_percent'
    aggregation: 'Maximum'
    operator: 'GreaterThan'
    threshold: 80
    window: 'PT1H'
    severity: 2
  }
  {
    key: 'db-load'
    description: 'The GIIM database has been over 90% busy for 15 minutes; consider a larger size.'
    scope: databaseId
    namespace: 'Microsoft.Sql/servers/databases'
    metric: dtuBased ? 'dtu_consumption_percent' : 'cpu_percent'
    aggregation: 'Average'
    operator: 'GreaterThan'
    threshold: 90
    window: 'PT15M'
    severity: 3
  }
]

resource alerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [for alert in metricAlerts: {
  name: '${name}-${alert.key}'
  location: 'global'
  tags: tags
  properties: {
    description: alert.description
    severity: alert.severity
    enabled: true
    scopes: [alert.scope]
    evaluationFrequency: 'PT5M'
    windowSize: alert.window
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: alert.metric
          metricNamespace: alert.namespace
          metricName: alert.metric
          timeAggregation: alert.aggregation
          operator: alert.operator
          threshold: alert.threshold
        }
      ]
    }
    actions: [{ actionGroupId: team.id }]
  }
}]

resource intuneSyncFailed 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${name}-intune-sync-failed'
  location: location
  tags: tags
  properties: {
    description: 'The Intune device sync failed. GIIM retries at the next interval; check the workers logs.'
    severity: 2
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT1H'
    windowSize: 'PT1H'
    criteria: {
      allOf: [
        {
          query: 'traces | where message startswith "Intune sync failed"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [team.id] }
  }
}
