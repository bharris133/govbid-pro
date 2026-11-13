param location string
param namePrefix string
param tags object
param addressSpace string = '10.40.0.0/16'

// Container Apps environment subnet (workload profiles: /27 minimum; /23 leaves room to scale).
// Service endpoints let Storage and Key Vault deny all public traffic except this subnet.
resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${namePrefix}-vnet'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [addressSpace] }
    subnets: [
      {
        name: 'apps'
        properties: {
          addressPrefix: cidrSubnet(addressSpace, 23, 0)
          delegations: [{ name: 'aca', properties: { serviceName: 'Microsoft.App/environments' } }]
          serviceEndpoints: [{ service: 'Microsoft.Storage' }, { service: 'Microsoft.KeyVault' }]
        }
      }
      {
        name: 'postgres'
        properties: {
          addressPrefix: cidrSubnet(addressSpace, 24, 2)
          delegations: [{ name: 'pg', properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' } }]
        }
      }
    ]
  }
}

resource postgresDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: '${namePrefix}.private.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresDns
  name: '${namePrefix}-vnet-link'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnet.id }
    registrationEnabled: false
  }
}

output appsSubnetId string = vnet.properties.subnets[0].id
output postgresSubnetId string = vnet.properties.subnets[1].id
output postgresPrivateDnsZoneId string = postgresDns.id
