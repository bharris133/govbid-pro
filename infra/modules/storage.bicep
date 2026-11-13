param location string
param namePrefix string
param tags object
param appsSubnetId string
param webIdentityPrincipalId string
@allowed(['Standard_LRS', 'Standard_ZRS', 'Standard_GRS', 'Standard_GZRS'])
@description('Redundancy is an OPS-01 decision (cost/residency/recovery).')
param skuName string
@description('Blob soft-delete window in days. Aligns with the provisional deleted-document recovery period (D-02).')
param blobSoftDeleteDays int

var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// Private by default: no public blob access, no shared keys (managed identity only), TLS 1.2+.
resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('${replace(namePrefix, '-', '')}st${uniqueString(resourceGroup().id)}', 24)
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: skuName }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      virtualNetworkRules: [{ id: appsSubnetId, action: 'Allow' }]
    }
    encryption: {
      keySource: 'Microsoft.Storage'
      services: { blob: { enabled: true } }
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: blobSoftDeleteDays }
    containerDeleteRetentionPolicy: { enabled: true, days: blobSoftDeleteDays }
    isVersioningEnabled: false
  }
}

resource documents 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'documents'
  properties: { publicAccess: 'None' }
}

resource dataProtection 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'dataprotection'
  properties: { publicAccess: 'None' }
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, webIdentityPrincipalId, storageBlobDataContributor)
  properties: {
    principalId: webIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
  }
}

output blobServiceUri string = account.properties.primaryEndpoints.blob
