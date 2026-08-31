using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Hark.Installer;

/// <summary>
/// Drives the Azure CLI to stand up HARK's infrastructure from the installer — the same
/// <c>az deployment sub create</c> path the <c>Provision Azure Infra</c> pipeline runs, against the
/// Bicep embedded in this exe. Requires <c>az</c> on PATH and a prior <c>az login</c> to the target
/// subscription; auth and role assignments use the signed-in user (keyless), never a stored secret.
/// </summary>
internal static class AzureProvisioner
{
    /// <summary>Inputs collected from the provisioning card.</summary>
    internal sealed record Options(
        string Location,
        string ResourceGroup,
        string? AccountNameOverride,
        string PrincipalId,
        bool DeployOpenAi,
        bool DeployImage);

    /// <summary>The outcome of a provisioning run: success + the config the app should read, or an error.</summary>
    internal sealed record Result(bool Ok, string Message, IReadOnlyDictionary<string, string>? Config);

    /// <summary>True when the Azure CLI is installed and callable.</summary>
    internal static async Task<bool> IsAzAvailableAsync()
    {
        var (code, _, _) = await RunAzAsync("version -o json", CancellationToken.None).ConfigureAwait(false);
        return code == 0;
    }

    /// <summary>The active subscription id, or null when not signed in.</summary>
    internal static async Task<string?> GetSubscriptionIdAsync()
    {
        var (code, stdout, _) = await RunAzAsync("account show --query id -o tsv", CancellationToken.None).ConfigureAwait(false);
        return code == 0 && stdout.Length > 0 ? stdout : null;
    }

    /// <summary>The signed-in user's object (principal) id for the data-plane role assignments, or null.</summary>
    internal static async Task<string?> GetPrincipalIdAsync()
    {
        var (code, stdout, _) = await RunAzAsync("ad signed-in-user show --query id -o tsv", CancellationToken.None).ConfigureAwait(false);
        return code == 0 && stdout.Length > 0 ? stdout : null;
    }

    /// <summary>Runs the subscription-scoped deployment and maps its outputs to the app's config keys.</summary>
    internal static async Task<Result> ProvisionAsync(Options o, string templateFile, CancellationToken ct)
    {
        var name = "hark-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        var create = new StringBuilder();
        create.Append("deployment sub create");
        create.Append(" --name ").Append(name);
        create.Append(" --location ").Append(o.Location);
        create.Append(" --template-file \"").Append(templateFile).Append('"');
        create.Append(" --parameters");
        create.Append(" location=").Append(o.Location);
        create.Append(" resourceGroupName=").Append(o.ResourceGroup);
        create.Append(" principalId=").Append(o.PrincipalId);
        create.Append(" principalType=User");
        create.Append(" deployOpenAi=").Append(o.DeployOpenAi ? "true" : "false");
        create.Append(" deployOpenAiImage=").Append(o.DeployImage ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(o.AccountNameOverride))
            create.Append(" openAiAccountName=").Append(o.AccountNameOverride);
        // -o none: az can throw "content ... already consumed" while formatting the create response even
        // though the deployment SUCCEEDED, so we don't read outputs here — nor trust this exit code.
        create.Append(" --only-show-errors -o none");

        var (_, _, createErr) = await RunAzAsync(create.ToString(), ct).ConfigureAwait(false);

        // Verify the ACTUAL deployment state with a separate call (immune to the create-response bug).
        var (stateCode, state, _) = await RunAzAsync(
            $"deployment sub show --name {name} --query properties.provisioningState -o tsv --only-show-errors", ct)
            .ConfigureAwait(false);

        if (stateCode != 0 || !string.Equals(state, "Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var msg = createErr.Length > 0 ? createErr
                : state.Length > 0 ? $"Deployment state: {state}"
                : "Deployment did not complete.";
            return new Result(false, Tail(msg), null);
        }

        // Succeeded — fetch outputs separately.
        var (outCode, outputs, outErr) = await RunAzAsync(
            $"deployment sub show --name {name} --query properties.outputs -o json --only-show-errors", ct)
            .ConfigureAwait(false);
        if (outCode != 0)
            return new Result(false, Tail(outErr.Length > 0 ? outErr : "Couldn't read deployment outputs."), null);

        try
        {
            return new Result(true, "Provisioned.", ParseOutputs(outputs));
        }
        catch (Exception ex)
        {
            return new Result(false, $"Deployed, but its outputs couldn't be read ({ex.Message}).", null);
        }
    }

    /// <summary>Maps the deployment's outputs object to the HARK_* config keys the app reads.</summary>
    private static Dictionary<string, string> ParseOutputs(string outputsJson)
    {
        using var doc = JsonDocument.Parse(outputsJson);
        var root = doc.RootElement;
        string Val(string name) =>
            root.TryGetProperty(name, out var o) && o.TryGetProperty("value", out var v)
                ? v.GetString() ?? string.Empty
                : string.Empty;

        var map = new Dictionary<string, string>();
        void Put(string key, string value) { if (value.Length > 0) map[key] = value; }

        Put("HARK_SPEECH_REGION", Val("speechRegion"));
        Put("HARK_SPEECH_RESOURCE_ID", Val("speechResourceId"));
        Put("HARK_AOAI_ENDPOINT", Val("openAiEndpoint"));
        Put("HARK_AOAI_DEPLOYMENT", Val("openAiDeployment"));
        // FLUX is the effective render tier; fall back to the gpt-image deployment when only that was deployed.
        var image = Val("fluxDeployment");
        if (image.Length == 0) image = Val("openAiImageDeployment");
        Put("HARK_AOAI_IMAGE_DEPLOYMENT", image);
        return map;
    }

    /// <summary>Runs <c>az</c> via cmd.exe (az is a .cmd shim) and captures its output.</summary>
    private static async Task<(int code, string stdout, string stderr)> RunAzAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c az " + args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start the Azure CLI.");
            var outTask = p.StandardOutput.ReadToEndAsync(ct);
            var errTask = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return (p.ExitCode, (await outTask.ConfigureAwait(false)).Trim(), (await errTask.ConfigureAwait(false)).Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    /// <summary>Keeps the last chunk of an error so the UI shows the salient tail, not a wall of text.</summary>
    private static string Tail(string s)
    {
        s = s.Trim();
        return s.Length > 600 ? "…" + s[^600..] : s;
    }
}
