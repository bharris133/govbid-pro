param location string
param namePrefix string
param tags object
param subnetId string
param privateDnsZoneId string
param administratorLogin string
@secure()
param administratorPassword string
@description('SKU name, e.g. Standard_B1ms (dev). Production SKU is an OPS-01 decision.')
param skuName string
@allowed(['Burstable', 'GeneralPurpose', 'MemoryOptimized'])
param skuTier string
param storageSizeGB int
@description('Backup retention in days. Provisional D-02 target is 30; confirm actual managed-service semantics (RET-03).')
@minValue(7)
@maxValue(35)
param backupRetentionDays int
@allowed(['Enabled', 'Disabled'])
param geoRedundantBackup string
@allowed(['Disabled', 'SameZone', 'ZoneRedundant'])
param highAvailabilityMode string

// Private access only (VNet-integrated); no public endpoint (CLOUD-03).
resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${namePrefix}-pg'
  location: location
  tags: tags
  sku: { name: skuName, tier: skuTier }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    storage: { storageSizeGB: storageSizeGB, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: backupRetentionDays, geoRedundantBackup: geoRedundantBackup }
    highAvailability: { mode: highAvailabilityMode }
    network: {
      delegatedSubnetResourceId: subnetId
      privateDnsZoneArmResourceId: privateDnsZoneId
      publicNetworkAccess: 'Disabled'
    }
    authConfig: { activeDirectoryAuth: 'Disabled', passwordAuth: 'Enabled' }
  }
}

resource requireTls 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'require_secure_transport'
  properties: { value: 'on', source: 'user-override' }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: 'govbidpro'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

output fqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
