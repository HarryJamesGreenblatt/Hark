// HARK — Azure infrastructure (Infrastructure-as-Code)
//
// Subscription-scoped deployment that stands up everything HARK needs on a fresh subscription:
//   • an Azure AI Speech resource (kind=SpeechServices, S0) for the core transcription pipeline
//   • (optional) an Azure OpenAI resource + chat deployment for the desktop SUMMARY recaps
//   • keyless RBAC role assignments granting a signed-in principal the data-plane roles
//
// Auth stays keyless (Entra ID) end-to-end — no account keys are ever emitted or stored.
//
// Deploy:
//   az deployment sub create --location eastus2 --template-file infra/main.bicep \
//     --parameters infra/main.parameters.json principalId=<your-object-id>

targetScope = 'subscription'

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

@description('Azure region for the resource group and all resources, e.g. eastus2.')
param location string = 'eastus2'

@description('Name of the resource group to create/use.')
param resourceGroupName string = 'rg-hark'

@description('Optional override for the Azure AI Speech account name (also its custom subdomain). Leave empty to auto-generate a globally-unique name. Custom subdomains are GLOBALLY unique across Azure.')
param speechAccountName string = ''

@description('Object (principal) id that should receive the data-plane roles. Get it via: az ad signed-in-user show --query id -o tsv')
param principalId string

@description('Principal type for the role assignments. Use "User" for an interactive sign-in, "ServicePrincipal" for a pipeline identity.')
@allowed([
  'User'
  'ServicePrincipal'
  'Group'
])
param principalType string = 'User'

@description('Deploy the optional Azure OpenAI resource + chat deployment used by the desktop SUMMARY view.')
param deployOpenAi bool = false

@description('Optional override for the Azure OpenAI account name (also its custom subdomain). Leave empty to auto-generate a globally-unique name.')
param openAiAccountName string = ''

@description('Chat model deployment name for recaps.')
param openAiDeploymentName string = 'gpt-4.1-mini'

@description('Chat model name.')
param openAiModelName string = 'gpt-4.1-mini'

@description('Chat model version.')
param openAiModelVersion string = '2025-04-14'

@description('Deployment SKU capacity (in thousands of tokens per minute).')
param openAiCapacity int = 10

// Short, stable, globally-distinct suffix derived from the target subscription. Keeps default
// resource names unique across subscriptions while remaining deterministic across re-runs.
var uniqueSuffix = substring(uniqueString(subscription().id), 0, 6)

// Effective names: honor an explicit override, otherwise fall back to the globally-unique default.
var effectiveSpeechName = empty(speechAccountName) ? 'spch-hark-${uniqueSuffix}' : speechAccountName
var effectiveOpenAiName = empty(openAiAccountName) ? 'aoai-hark-${uniqueSuffix}' : openAiAccountName

// ---------------------------------------------------------------------------
// Resource group
// ---------------------------------------------------------------------------

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

// ---------------------------------------------------------------------------
// Speech (required) — deployed into the resource group via a module
// ---------------------------------------------------------------------------

module speech 'modules/speech.bicep' = {
  name: 'hark-speech'
  scope: rg
  params: {
    location: location
    speechAccountName: effectiveSpeechName
    principalId: principalId
    principalType: principalType
  }
}

// ---------------------------------------------------------------------------
// Azure OpenAI (optional) — recaps for the desktop overlay
// ---------------------------------------------------------------------------

module openAi 'modules/openai.bicep' = if (deployOpenAi) {
  name: 'hark-openai'
  scope: rg
  params: {
    location: location
    openAiAccountName: effectiveOpenAiName
    deploymentName: openAiDeploymentName
    modelName: openAiModelName
    modelVersion: openAiModelVersion
    capacity: openAiCapacity
    principalId: principalId
    principalType: principalType
  }
}

// ---------------------------------------------------------------------------
// Outputs — feed these straight into `dotnet user-secrets`
// ---------------------------------------------------------------------------

@description('The Speech resource region (HARK_SPEECH_REGION).')
output speechRegion string = location

@description('The Speech resource ARM id (HARK_SPEECH_RESOURCE_ID).')
output speechResourceId string = speech.outputs.resourceId

@description('The Azure OpenAI endpoint (HARK_AOAI_ENDPOINT), empty when not deployed.')
output openAiEndpoint string = deployOpenAi ? openAi!.outputs.endpoint : ''

@description('The Azure OpenAI chat deployment name (HARK_AOAI_DEPLOYMENT), empty when not deployed.')
output openAiDeployment string = deployOpenAi ? openAiDeploymentName : ''
