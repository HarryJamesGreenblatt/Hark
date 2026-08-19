# HARK — convenience launcher
# Sets the (non-sensitive) region for the current session and runs the CLI.
# Auth is keyless (Entra ID) via your Azure CLI / Visual Studio sign-in — no keys here.
#
# The Speech resource ARM id embeds your subscription id, so it's never hardcoded here — it's
# read from dotnet user-secrets (dev-machine-local, never committed) or HARK_SPEECH_RESOURCE_ID.
# One-time setup: dotnet user-secrets set HARK_SPEECH_RESOURCE_ID "<arm-id>" --project Hark.Cli

param(
	[string]$Out  = "transcript.txt",
	[string]$Json,
	[string]$Srt,
	[string]$Language,
	[switch]$Quiet
)

$env:HARK_SPEECH_REGION = "eastus2"

$harkArgs = @()
if ($Out)      { $harkArgs += @("--out", $Out) }
if ($Json)     { $harkArgs += @("--json", $Json) }
if ($Srt)      { $harkArgs += @("--srt", $Srt) }
if ($Language) { $harkArgs += @("--language", $Language) }
if ($Quiet)    { $harkArgs += "--quiet" }

dotnet run --project (Join-Path $PSScriptRoot "Hark.Cli") -- @harkArgs
