@description('Azure region. No default: region selection is gate OPS-01.')
param location string
param namePrefix string
param tags object
@description('Log retention in days for operational logs.')
param logRetentionDays int

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: logRetentionDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-appi'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    DisableLocalAuth: true
  }
}

output logAnalyticsWorkspaceId string = logs.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
