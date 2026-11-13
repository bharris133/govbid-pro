param location string
param namePrefix string
param tags object
param appsSubnetId string
param logAnalyticsWorkspaceId string
param webIdentityId string
param webIdentityClientId string
param containerImage string
param connectionSecretUri string
param blobServiceUri string
param dataProtectionKeyUri string
@secure()
param appInsightsConnectionString string
param minReplicas int
param maxReplicas int

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-cae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2023-09-01').primarySharedKey
      }
    }
    vnetConfiguration: { infrastructureSubnetId: appsSubnetId, internal: false }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
  }
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-web'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${webIdentityId}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: [
        { name: 'db-connection', keyVaultUrl: connectionSecretUri, identity: webIdentityId }
        { name: 'appinsights-connection', value: appInsightsConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: containerImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__GovBidPro', secretRef: 'db-connection' }
            { name: 'Storage__Provider', value: 'AzureBlob' }
            { name: 'Storage__BlobServiceUri', value: blobServiceUri }
            { name: 'Storage__ContainerName', value: 'documents' }
            { name: 'Storage__ManagedIdentityClientId', value: webIdentityClientId }
            { name: 'Storage__DataProtectionKeyVaultKeyUri', value: dataProtectionKeyUri }
            { name: 'AZURE_CLIENT_ID', value: webIdentityClientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection' }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 15 }
          ]
        }
      ]
      scale: { minReplicas: minReplicas, maxReplicas: maxReplicas }
    }
  }
}

output webFqdn string = web.properties.configuration.ingress.fqdn
