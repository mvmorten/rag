// Resource-group-scoped deployment of everything the app needs.
// Auth is Entra ID throughout: one user-assigned managed identity, no keys, so there is no Key Vault.
targetScope = 'resourceGroup'

@description('Short lowercase prefix used in resource names.')
@minLength(3)
@maxLength(12)
param baseName string = 'ragdev'

@description('Region for most resources.')
param location string = resourceGroup().location

@description('Azure AI Search is capacity-constrained in some regions; allow a different one.')
param searchLocation string = location

@description('Container image for the API+UI. The first deploy uses a placeholder until the pipeline pushes a real image.')
param image string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Whether the container image is the real app (enables the app health probe and settings).')
param deployApp bool = false

@description('Optional object id of a person who should be able to run ingestion against the index.')
param developerPrincipalId string = ''

param chatModel object = { name: 'gpt-5.4-mini', version: '2026-03-17', capacity: 100 }
param judgeModel object = { name: 'gpt-5.4', version: '2026-03-05', capacity: 50 }
param embeddingModel object = { name: 'text-embedding-3-small', version: '1', capacity: 100 }

var suffix = uniqueString(resourceGroup().id)
var uniqueName = '${baseName}-${suffix}'

// Built-in role definition ids.
var roles = {
  openAIUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  searchIndexDataReader: '1407120a-92aa-4202-b7e9-c0e197c71c8f'
  searchIndexDataContributor: '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
  searchServiceContributor: '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${uniqueName}'
  location: location
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${uniqueName}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: replace('acr${uniqueName}', '-', '')
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'aoai-${uniqueName}'
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: 'aoai-${uniqueName}'
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

// Deployments must be created one at a time or the service returns a conflict.
resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: embeddingModel.name
  sku: { name: 'Standard', capacity: embeddingModel.capacity }
  properties: { model: { format: 'OpenAI', name: embeddingModel.name, version: embeddingModel.version } }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: chatModel.name
  dependsOn: [embeddingDeployment]
  sku: { name: 'GlobalStandard', capacity: chatModel.capacity }
  properties: { model: { format: 'OpenAI', name: chatModel.name, version: chatModel.version } }
}

resource judgeDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: judgeModel.name
  dependsOn: [chatDeployment]
  sku: { name: 'GlobalStandard', capacity: judgeModel.capacity }
  properties: { model: { format: 'OpenAI', name: judgeModel.name, version: judgeModel.version } }
}

resource search 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: 'srch-${uniqueName}'
  location: searchLocation
  sku: { name: 'basic' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    semanticSearch: 'free'
    disableLocalAuth: false // Entra auth is used by the app; keys stay enabled so `az` tooling and the portal still work.
    authOptions: { aadOrApiKey: { aadAuthFailureMode: 'http401WithBearerChallenge' } }
  }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${uniqueName}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${uniqueName}'
  location: location
  tags: { 'azd-service-name': 'app' }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [acrPull]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: {
        external: true
        targetPort: deployApp ? 8080 : 80
        transport: 'auto'
      }
      registries: deployApp ? [
        { server: registry.properties.loginServer, identity: identity.id }
      ] : []
    }
    template: {
      containers: [
        {
          name: 'app'
          image: image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
            { name: 'Rag__OpenAIEndpoint', value: openai.properties.endpoint }
            { name: 'Rag__SearchEndpoint', value: 'https://${search.name}.search.windows.net' }
            { name: 'Rag__ChatDeployment', value: chatModel.name }
            { name: 'Rag__EmbeddingDeployment', value: embeddingModel.name }
            { name: 'Rag__JudgeDeployment', value: judgeModel.name }
          ]
          probes: deployApp ? [
            { type: 'Liveness', httpGet: { path: '/health', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/health', port: 8080 } }
          ] : []
        }
      ]
      // Public endpoint that spends model tokens: keep the ceiling low.
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

// --- Role assignments -------------------------------------------------------

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, roles.acrPull)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
  }
}

resource appOpenAI 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: openai
  name: guid(openai.id, identity.id, roles.openAIUser)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.openAIUser)
  }
}

// The running app only reads the index; ingestion is a separate, privileged job.
resource appSearch 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: search
  name: guid(search.id, identity.id, roles.searchIndexDataReader)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.searchIndexDataReader)
  }
}

resource devOpenAI 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(developerPrincipalId)) {
  scope: openai
  name: guid(openai.id, developerPrincipalId, roles.openAIUser)
  properties: {
    principalId: developerPrincipalId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.openAIUser)
  }
}

resource devSearchData 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(developerPrincipalId)) {
  scope: search
  name: guid(search.id, developerPrincipalId, roles.searchIndexDataContributor)
  properties: {
    principalId: developerPrincipalId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.searchIndexDataContributor)
  }
}

resource devSearchService 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(developerPrincipalId)) {
  scope: search
  name: guid(search.id, developerPrincipalId, roles.searchServiceContributor)
  properties: {
    principalId: developerPrincipalId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.searchServiceContributor)
  }
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output appName string = app.name
output openAIEndpoint string = openai.properties.endpoint
output searchEndpoint string = 'https://${search.name}.search.windows.net'
