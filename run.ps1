# HARK — convenience launcher
# Sets the Speech resource configuration for the current session and runs the CLI.
# Auth is keyless (Entra ID) via your Azure CLI / Visual Studio sign-in — no keys here.

param(
	[string]$Out  = "transcript.txt",
	[string]$Json,
	[string]$Srt,
	[string]$Language,
	[switch]$Quiet
)

$env:HARK_SPEECH_REGION      = "eastus2"
$env:HARK_SPEECH_RESOURCE_ID = "/subscriptions/REDACTED-SUBSCRIPTION-ID/resourceGroups/rg-hark/providers/Microsoft.CognitiveServices/accounts/spch-hark"

$harkArgs = @()
if ($Out)      { $harkArgs += @("--out", $Out) }
if ($Json)     { $harkArgs += @("--json", $Json) }
if ($Srt)      { $harkArgs += @("--srt", $Srt) }
if ($Language) { $harkArgs += @("--language", $Language) }
if ($Quiet)    { $harkArgs += "--quiet" }

dotnet run --project (Join-Path $PSScriptRoot "Hark.Cli") -- @harkArgs
