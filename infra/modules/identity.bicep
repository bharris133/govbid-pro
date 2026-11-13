param location string
param namePrefix string
param tags object

// One user-assigned identity for the web app: no credentials in images or configuration (CLOUD-03).
resource webIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-web-id'
  location: location
  tags: tags
}

output id string = webIdentity.id
output principalId string = webIdentity.properties.principalId
output clientId string = webIdentity.properties.clientId
