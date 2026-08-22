// HARK — Azure AI Speech resource + keyless role assignment (resource-group scope).

@description('Azure region for the Speech account.')
param location string

@description('Name of the Speech account (also used as its custom subdomain).')
param speechAccountName string

@description('Object (principal) id to grant the Cognitive Services Speech User role.')
param principalId string

@description('Principal type for the role assignment.')
param principalType string

// Cognitive Services Speech User — data-plane role used for keyless transcription.
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles
var speechUserRoleId = 'f2dc8367-1007-4938-bd23-fe263f013447'

resource speech 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: speechAccountName
  location: location
  kind: 'SpeechServices'
  sku: {
    name: 'S0'
  }
  properties: {
    // Custom subdomain is required for keyless (Entra ID / token) auth.
    customSubDomainName: speechAccountName
    publicNetworkAccess: 'Enabled'
  }
}

resource speechRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(speech.id, principalId, speechUserRoleId)
  scope: speech
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', speechUserRoleId)
    principalId: principalId
    principalType: principalType
  }
}

@description('The full ARM resource id of the Speech account.')
output resourceId string = speech.id
