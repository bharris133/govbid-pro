// Development environment parameters. Region and secrets come from the deployment environment;
// nothing here selects a production region or embeds a credential (gate OPS-01).
using '../main.bicep'

param location = readEnvironmentVariable('AZURE_LOCATION', 'set-AZURE_LOCATION')
param environmentName = 'dev'
param containerImage = readEnvironmentVariable('GOVBID_IMAGE', 'set-GOVBID_IMAGE')
param postgresAdministratorPassword = readEnvironmentVariable('GOVBID_PG_ADMIN_PASSWORD', '')
param postgresSkuName = 'Standard_B1ms'
param postgresSkuTier = 'Burstable'
param storageSkuName = 'Standard_LRS'
param minReplicas = 1
param maxReplicas = 2
