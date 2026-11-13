// GovBid Pro Azure deployment skeleton (owner decision D-01 / CLOUD-01..04).
//
// Status: SKELETON. Compiles in CI; never deployed by CI. Production deployment is blocked on gates
// OPS-01 (subscription/tenant, region, budget, SKUs, alerts, support access), OPS-02 (RTO/RPO,
// backup/restore objectives, incident contacts) and LEG-01 (retention/deletion SLA). Azure commercial
// cloud for Release 1 does not imply suitability for FCI/CUI, classified data, FedRAMP or agency use (CLOUD-04).
targetScope = 'resourceGroup'

@description('Azure region. Intentionally no default: production region is an unresolved OPS-01 decision.')
param location string

@description('Environment name, e.g. dev or test. Production parameters are not committed until OPS-01 is approved.')
@allowed(['dev', 'test', 'staging', 'prod'])
param environmentName string

@description('Short resource name prefix.')
@maxLength(12)
param appName string = 'govbid'

@description('Container image reference (registry/repository@digest preferred).')
param containerImage string

@description('PostgreSQL administrator login.')
param postgresAdministratorLogin string = 'govbidadmin'

@secure()
@description('PostgreSQL administrator password. Supplied at deployment time from a secret store; never committed.')
param postgresAdministratorPassword string

param postgresSkuName string = 'Standard_B1ms'
@allowed(['Burstable', 'GeneralPurpose', 'MemoryOptimized'])
param postgresSkuTier string = 'Burstable'
param postgresStorageSizeGB int = 32
@allowed(['Enabled', 'Disabled'])
param postgresGeoRedundantBackup string = 'Disabled'
@allowed(['Disabled', 'SameZone', 'ZoneRedundant'])
param postgresHighAvailability string = 'Disabled'

@description('Provisional D-02 database backup retention target (days). Subject to LEG-01/OPS-02.')
param databaseBackupRetentionDays int = 30

@description('Provisional D-02 deleted-document recovery window (days); used for blob soft delete.')
param deletedDocumentRecoveryDays int = 30

@description('Operational log retention (days). Security audit retention (12 months) is held in the application database; tamper-evident export is a P1.7 item.')
param logRetentionDays int = 90

@allowed(['Standard_LRS', 'Standard_ZRS', 'Standard_GRS', 'Standard_GZRS'])
param storageSkuName string = 'Standard_LRS'

param minReplicas int = 1
param maxReplicas int = 3

var namePrefix = '${appName}-${environmentName}'
var tags = {
  application: 'govbid-pro'
  environment: environmentName
  dataScope: 'R1-commercial-no-FCI-CUI-classified'
  managedBy: 'bicep'
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { location: location, namePrefix: namePrefix, tags: tags, logRetentionDays: logRetentionDays }
}

module network 'modules/network.bicep' = {
  name: 'network'
  params: { location: location, namePrefix: namePrefix, tags: tags }
}

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: { location: location, namePrefix: namePrefix, tags: tags }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    subnetId: network.outputs.postgresSubnetId
    privateDnsZoneId: network.outputs.postgresPrivateDnsZoneId
    administratorLogin: postgresAdministratorLogin
    administratorPassword: postgresAdministratorPassword
    skuName: postgresSkuName
    skuTier: postgresSkuTier
    storageSizeGB: postgresStorageSizeGB
    backupRetentionDays: databaseBackupRetentionDays
    geoRedundantBackup: postgresGeoRedundantBackup
    highAvailabilityMode: postgresHighAvailability
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    appsSubnetId: network.outputs.appsSubnetId
    webIdentityPrincipalId: identity.outputs.principalId
    databaseConnectionString: 'Host=${postgres.outputs.fqdn};Database=${postgres.outputs.databaseName};Username=${postgresAdministratorLogin};Password=${postgresAdministratorPassword};SSL Mode=Require'
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    appsSubnetId: network.outputs.appsSubnetId
    webIdentityPrincipalId: identity.outputs.principalId
    skuName: storageSkuName
    blobSoftDeleteDays: deletedDocumentRecoveryDays
  }
}

module apps 'modules/containerapps.bicep' = {
  name: 'containerapps'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    appsSubnetId: network.outputs.appsSubnetId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    webIdentityId: identity.outputs.id
    webIdentityClientId: identity.outputs.clientId
    containerImage: containerImage
    connectionSecretUri: keyVault.outputs.connectionSecretUri
    blobServiceUri: storage.outputs.blobServiceUri
    dataProtectionKeyUri: keyVault.outputs.dataProtectionKeyUri
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    minReplicas: minReplicas
    maxReplicas: maxReplicas
  }
}

output webFqdn string = apps.outputs.webFqdn
