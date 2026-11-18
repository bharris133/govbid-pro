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
@description('Turn on Microsoft Defender for Storage on-upload malware scanning for this account (ADR-0005).')
param malwareScanningEnabled bool
@description('Monthly scanning cap in GB for this account. Uploads beyond the cap are not scanned and stay unavailable until next month (fail closed).')
param malwareScanningCapGBPerMonth int

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
      // Defender for Storage adds its scanner here so it can read blobs behind this firewall. It must be declared,
      // or every redeploy of networkAcls would remove it and scans would fail ("could not access the blob").
      resourceAccessRules: malwareScanningEnabled
        ? [{ tenantId: tenant().tenantId, resourceId: '${subscription().id}/providers/Microsoft.Security/datascanners/StorageDataScanner' }]
        : []
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

// Defender for Storage (per-account plan) with on-upload malware scanning. Azure writes the verdict to the blob
// index tag "Malware Scanning scan result"; the app (Documents:MalwareScanMode=AzureDefender) reads it and keeps
// files undownloadable until it says "No threats found". Billing: per protected account per month plus per GB scanned.
resource defenderForStorage 'Microsoft.Security/defenderForStorageSettings@2022-12-01-preview' = if (malwareScanningEnabled) {
  scope: account
  name: 'current'
  properties: {
    isEnabled: true
    overrideSubscriptionLevelSettings: true
    malwareScanning: {
      onUpload: {
        isEnabled: true
        capGBPerMonth: malwareScanningCapGBPerMonth
      }
    }
    sensitiveDataDiscovery: { isEnabled: false }
  }
}

// Least privilege for reading verdicts: the app can read blob index tags but not write them, so it can never
// mark its own uploads clean. (Storage Blob Data Contributor does not include tag read; Owner would include tag write.)
resource tagReaderRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = if (malwareScanningEnabled) {
  name: guid(resourceGroup().id, 'govbid-blob-index-tag-reader')
  properties: {
    roleName: 'GovBid Blob Index Tag Reader (${resourceGroup().name})'
    description: 'Read blob index tags (malware scan verdicts written by Defender for Storage).'
    type: 'CustomRole'
    assignableScopes: [resourceGroup().id]
    permissions: [
      {
        actions: []
        notActions: []
        dataActions: ['Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read']
        notDataActions: []
      }
    ]
  }
}

resource tagReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (malwareScanningEnabled) {
  scope: account
  name: guid(account.id, webIdentityPrincipalId, 'govbid-blob-index-tag-reader')
  properties: {
    principalId: webIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: tagReaderRole.id
  }
}

output blobServiceUri string = account.properties.primaryEndpoints.blob
