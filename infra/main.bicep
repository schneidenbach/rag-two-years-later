// Ignite "RAG: Two Years Later" demo infrastructure.
// Foundry (AIServices) + project + model deployments, Azure AI Search (Basic, semantic ranker),
// PostgreSQL Flexible Server (pgvector), Storage (document store).
targetScope = 'resourceGroup'

param location string = resourceGroup().location
param searchLocation string = 'eastus'
param pgLocation string = 'westus3'
param pgServerName string = '${prefix}-wo-${uniqueString(resourceGroup().id)}'
param prefix string = 'lakeshore'
@secure()
param pgAdminPassword string
param pgAdminUser string = 'lakeshoreadmin'
param clientIp string
@description('Object id of the person running the demo (az ad signed-in-user show --query id -o tsv). Gets data-plane roles on Foundry, Search and Storage.')
param userPrincipalId string = ''

var suffix = uniqueString(resourceGroup().id)
var foundryName = '${prefix}-foundry-${suffix}'

// ---------- Foundry ----------
resource foundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: foundryName
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: foundryName
    allowProjectManagement: true
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundry
  name: 'ignite-rag-demo'
  location: location
  dependsOn: [ embedDeployment ]
  identity: { type: 'SystemAssigned' }
  properties: {
    displayName: 'Ignite RAG Demo'
    description: 'RAG: Two Years Later - agentic search demos'
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: 'gpt-5.5'
  sku: { name: 'GlobalStandard', capacity: 500 }
  properties: { model: { format: 'OpenAI', name: 'gpt-5.5', version: '2026-04-24' } }
}

resource miniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: 'gpt-5.4-mini'
  dependsOn: [ chatDeployment ]
  sku: { name: 'GlobalStandard', capacity: 500 }
  properties: { model: { format: 'OpenAI', name: 'gpt-5.4-mini', version: '2026-03-17' } }
}

resource embedDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundry
  name: 'text-embedding-3-small'
  dependsOn: [ miniDeployment ]
  sku: { name: 'GlobalStandard', capacity: 350 }
  properties: { model: { format: 'OpenAI', name: 'text-embedding-3-small', version: '1' } }
}

// ---------- Azure AI Search ----------
resource search 'Microsoft.Search/searchServices@2025-05-01' = {
  name: '${prefix}-search-${suffix}'
  location: searchLocation
  sku: { name: 'basic' }
  identity: { type: 'SystemAssigned' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    semanticSearch: 'standard'
    authOptions: { aadOrApiKey: { aadAuthFailureMode: 'http401WithBearerChallenge' } }
    publicNetworkAccess: 'enabled'
  }
}

// Search MI -> Cognitive Services OpenAI User on Foundry (vectorizer + KB planning model)
resource searchToFoundry 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: foundry
  name: guid(foundry.id, search.id, 'openai-user')
  properties: {
    principalId: search.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
  }
}

// ---------- Storage (document store) ----------
resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: take('${prefix}docs${suffix}', 24)
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    allowSharedKeyAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

resource docsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'facilities-docs'
}

// ---------- PostgreSQL Flexible Server ----------
resource pg 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: pgServerName
  location: pgLocation
  // General Purpose because Burstable SKUs are unavailable on some subscription types (e.g. Visual Studio); switch to Standard_B1ms/Burstable if yours allows it.
  sku: { name: 'Standard_D2ds_v5', tier: 'GeneralPurpose' }
  properties: {
    version: '17'
    administratorLogin: pgAdminUser
    administratorLoginPassword: pgAdminPassword
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource pgVectorAllow 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: pg
  name: 'azure.extensions'
  properties: { value: 'VECTOR', source: 'user-override' }
}

resource pgDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: pg
  name: 'workorders'
  dependsOn: [ pgVectorAllow ]
}

resource pgFwClient 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: pg
  name: 'presenter-laptop'
  dependsOn: [ pgDb ]
  properties: { startIpAddress: clientIp, endIpAddress: clientIp }
}

// Hosted agents run on Microsoft-managed compute; allow Azure-originated connections.
resource pgFwAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: pg
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  dependsOn: [ pgFwClient ]
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}


// ---------- Container registry (hosted agent image) ----------
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${prefix}acr${suffix}'
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

// The Foundry project identity pulls the hosted agent image.
resource projectAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, project.id, 'acrpull')
  scope: acr
  properties: {
    principalId: project.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

// ---------- Presenter data-plane roles (the apps use your az login, not keys) ----------
var userRoles = [
  { scope: 'foundry', id: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' } // Cognitive Services OpenAI User
  { scope: 'foundry', id: '53ca6127-db72-4b80-b1b0-d745d6d5456d' } // Foundry User (agents)
  { scope: 'search', id: '7ca78c08-252a-4471-8644-bb5ff32d4ba0' }  // Search Service Contributor (index, KB)
  { scope: 'search', id: '8ebe5a00-799e-43f5-93ac-243d3dce84a7' }  // Search Index Data Contributor
  { scope: 'storage', id: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' } // Storage Blob Data Contributor
]

resource userFoundryRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for r in filter(userRoles, r => r.scope == 'foundry'): if (!empty(userPrincipalId)) {
  scope: foundry
  name: guid(foundry.id, userPrincipalId, r.id)
  properties: { principalId: userPrincipalId, principalType: 'User', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', r.id) }
}]

resource userSearchRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for r in filter(userRoles, r => r.scope == 'search'): if (!empty(userPrincipalId)) {
  scope: search
  name: guid(search.id, userPrincipalId, r.id)
  properties: { principalId: userPrincipalId, principalType: 'User', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', r.id) }
}]

resource userStorageRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for r in filter(userRoles, r => r.scope == 'storage'): if (!empty(userPrincipalId)) {
  scope: storage
  name: guid(storage.id, userPrincipalId, r.id)
  properties: { principalId: userPrincipalId, principalType: 'User', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', r.id) }
}]

output acrLoginServer string = acr.properties.loginServer
output foundryName string = foundry.name
output foundryEndpoint string = foundry.properties.endpoint
output openAIEndpoint string = 'https://${foundryName}.openai.azure.com'
output projectEndpoint string = 'https://${foundryName}.services.ai.azure.com/api/projects/${project.name}'
output projectName string = project.name
output searchName string = search.name
output searchEndpoint string = 'https://${search.name}.search.windows.net'
output storageName string = storage.name
output pgHost string = pg.properties.fullyQualifiedDomainName
output pgUser string = pgAdminUser
output pgServerName string = pg.name
output acrName string = acr.name
