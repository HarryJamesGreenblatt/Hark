// HARK � Azure OpenAI resource, chat deployment, and keyless role assignment (resource-group scope).

@description('Azure region for the Azure OpenAI account.')
param location string

@description('Name of the Azure OpenAI account (also used as its custom subdomain).')
param openAiAccountName string

@description('Chat model deployment name.')
param deploymentName string

@description('Chat model name.')
param modelName string

@description('Chat model version.')
param modelVersion string

@description('Deployment SKU capacity.')
param capacity int

@description('Object (principal) id to grant the Cognitive Services OpenAI User role.')
param principalId string

@description('Principal type for the role assignment.')
param principalType string

@description('Deploy a gpt-image image model deployment (the Vision crystal-ball render tier).')
param deployImage bool = false

@description('Image model deployment name.')
param imageDeploymentName string = 'gpt-image-1'

@description('Image model name.')
param imageModelName string = 'gpt-image-1'

@description('Image model version.')
param imageModelVersion string = '2025-04-15'

@description('Image deployment SKU capacity (requests per minute).')
param imageCapacity int = 1

// Cognitive Services OpenAI User � data-plane role used for keyless recaps.
var openAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiAccountName
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openAiAccountName
    publicNetworkAccess: 'Enabled'
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: deploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

// Optional gpt-image deployment — the Vision crystal-ball render tier. dependsOn the chat deployment
// because Cognitive Services serializes deployment operations per account (parallel creates conflict).
resource imageDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (deployImage) {
  parent: openAi
  name: imageDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: imageCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: imageModelName
      version: imageModelVersion
    }
  }
  dependsOn: [
    chatDeployment
  ]
}

resource openAiRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, principalId, openAiUserRoleId)
  scope: openAi
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', openAiUserRoleId)
    principalId: principalId
    principalType: principalType
  }
}

@description('The Azure OpenAI endpoint.')
output endpoint string = openAi.properties.endpoint

@description('The gpt-image deployment name (HARK_AOAI_IMAGE_DEPLOYMENT), empty when not deployed.')
output imageDeployment string = deployImage ? imageDeploymentName : ''
