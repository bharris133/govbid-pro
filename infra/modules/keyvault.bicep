param location string
param namePrefix string
param tags object
param appsSubnetId string
param webIdentityPrincipalId string
@secure()
@description('PostgreSQL connection string for the application, stored as a Key Vault secret.')
param databaseConnectionString string

// Public Azure built-in role definition IDs (Key Vault Secrets User, Key Vault Crypto User), not secrets.
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6' // gitleaks:allow
var keyVaultCryptoUser = '12338af0-0e69-4776-bea7-57ae8d297424' // gitleaks:allow

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('${replace(namePrefix, '-', '')}kv${uniqueString(resourceGroup().id)}', 24)
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      virtualNetworkRules: [{ id: appsSubnetId }]
    }
  }
}

resource connectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'govbid-db-connection'
  properties: { value: databaseConnectionString }
}

// Wraps ASP.NET Core Data Protection keys (auth cookies, antiforgery).
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: ['wrapKey', 'unwrapKey']
  }
}

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, webIdentityPrincipalId, keyVaultSecretsUser)
  properties: {
    principalId: webIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
  }
}

resource cryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: dataProtectionKey
  name: guid(dataProtectionKey.id, webIdentityPrincipalId, keyVaultCryptoUser)
  properties: {
    principalId: webIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultCryptoUser)
  }
}

output vaultUri string = vault.properties.vaultUri
output connectionSecretUri string = connectionSecret.properties.secretUri
output dataProtectionKeyUri string = dataProtectionKey.properties.keyUri
