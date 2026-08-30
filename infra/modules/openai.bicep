// HARK - Azure AI Foundry (AIServices) account: chat + image + FLUX deployments and keyless role
// assignments (resource-group scope). Hosts OpenAI-format models (gpt-4.1-mini chat, gpt-image-2) and
// the Black Forest Labs FLUX.2-pro provider model side-by-side on ONE endpoint (HARK uses a single
// HARK_AOAI_ENDPOINT for both the concept/chat and the render calls).

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
param imageDeploymentName string = 'gpt-image-2'

@description('Image model name.')
param imageModelName string = 'gpt-image-2'

@description('Image model version.')
param imageModelVersion string = '2026-04-21'

@description('Image deployment SKU capacity (requests per minute).')
param imageCapacity int = 1

@description('Deploy FLUX.2-pro (Black Forest Labs provider route) - the effective default Vision render tier.')
param deployFlux bool = true

@description('FLUX deployment name (HARK_AOAI_IMAGE_DEPLOYMENT when FLUX is the render tier).')
param fluxDeploymentName string = 'flux2-pro'

@description('FLUX model name.')
param fluxModelName string = 'FLUX.2-pro'

@description('FLUX model version.')
param fluxModelVersion string = '1'

@description('FLUX deployment SKU capacity (requests per minute).')
param fluxCapacity int = 10

// Cognitive Services OpenAI User - data-plane role for keyless recaps (OpenAI-format models).
var openAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
// Cognitive Services User - broader data-plane role covering the Black Forest Labs (FLUX) provider route.
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiAccountName
  location: location
  kind: 'AIServices'
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

// Optional gpt-image deployment - an OpenAI-format render option (FLUX.2-pro below is the effective
// default). dependsOn the chat deployment because Cognitive Services serializes deployment operations
// per account (parallel creates conflict).
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

// FLUX.2-pro (Black Forest Labs provider) - the effective default Vision render tier, on the same
// endpoint as the OpenAI-format models. Serialized after the image deployment (Cognitive Services
// serializes deployment operations per account; parallel creates conflict).
resource fluxDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (deployFlux) {
  parent: openAi
  name: fluxDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: fluxCapacity
  }
  properties: {
    model: {
      format: 'Black Forest Labs'
      name: fluxModelName
      version: fluxModelVersion
    }
  }
  dependsOn: [
    chatDeployment
    imageDeployment
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

// Cognitive Services User - covers the FLUX provider data-plane route (the OpenAI User role does not).
resource cognitiveServicesUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, principalId, cognitiveServicesUserRoleId)
  scope: openAi
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: principalId
    principalType: principalType
  }
}

@description('The Azure AI Foundry (AIServices) endpoint.')
output endpoint string = openAi.properties.endpoint

@description('The gpt-image deployment name, empty when not deployed.')
output imageDeployment string = deployImage ? imageDeploymentName : ''

@description('The FLUX deployment name (HARK_AOAI_IMAGE_DEPLOYMENT when FLUX is the render tier), empty when not deployed.')
output fluxDeployment string = deployFlux ? fluxDeploymentName : ''
