using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Images;

namespace Hark.Oracle.Vision;

/// <summary>
/// The image backend for <c>Oracle.Vision</c>'s <b>scene</b> class — a thin, keyless renderer that turns a
/// composed prompt into PNG bytes. <b>Provider-agnostic:</b> it drives either the Azure OpenAI
/// <c>ImageClient</c> (gpt-image) or a raw-HTTP <b>Black Forest Labs</b> route (FLUX), selected by the
/// <c>provider</c> ctor arg.
/// <para>
/// <b>Infra note:</b> requires an image deployment on the account (e.g. <c>flux2-pro</c> — the effective
/// default — or a <c>gpt-image</c> deployment), separate from the chat deployment. The concept tier works
/// without it; this tier renders only once an image model is provisioned. The didactic <b>diagram</b>
/// class is rendered <b>natively</b> by the overlay and does NOT use this renderer.
/// </para>
/// </summary>
public sealed class VisionRenderer
{
    #region Fields

    /// <summary>Shared HTTP client for the Black Forest Labs provider route (FLUX), which the OpenAI SDK can't reach.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>Entra token scope for Azure AI / Cognitive Services data-plane access.</summary>
    private static readonly string[] CognitiveScope = ["https://cognitiveservices.azure.com/.default"];

    /// <summary>Image client for the gpt-image OpenAI route; <see langword="null"/> on the FLUX provider path.</summary>
    private readonly ImageClient? _images;

    /// <summary>Optional quality tier; sent only when set, so non-gpt-image models (FLUX) aren't handed an unsupported enum.</summary>
    private readonly string? _quality;

    /// <summary>Black Forest Labs provider route segment (e.g. <c>flux-2-pro</c>); non-null selects the FLUX path.</summary>
    private readonly string? _providerRoute;

    /// <summary>The composed BFL provider URL, the model name, and the credential — used on the FLUX path.</summary>
    private readonly string? _providerUri;
    private readonly string? _deployment;
    private readonly TokenCredential? _credential;

    #endregion

    #region Constructor(s)

    /// <summary>Creates a renderer bound to an Azure OpenAI image deployment.</summary>
    /// <param name="endpoint">The resource endpoint, e.g. <c>https://my-aoai.openai.azure.com/</c>.</param>
    /// <param name="deployment">The image model deployment name (e.g. a <c>gpt-image-2</c> or <c>FLUX.2-pro</c> deployment).</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    /// <param name="quality">Optional quality tier (gpt-image <c>low</c>/<c>medium</c>/<c>high</c>); omit for model default / FLUX.</param>
    /// <param name="provider">Optional non-OpenAI provider route (e.g. <c>flux-2-pro</c>) served via the Black Forest Labs API; omit for the gpt-image OpenAI route.</param>
    public VisionRenderer(string endpoint, string deployment, TokenCredential? credential = null, string? quality = null, string? provider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment);

        credential ??= new DefaultAzureCredential();
        _quality = string.IsNullOrWhiteSpace(quality) ? null : quality;
        _providerRoute = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();

        if (_providerRoute is null)
        {
            // OpenAI-compatible route — gpt-image-*.
            _images = new AzureOpenAIClient(new Uri(endpoint), credential).GetImageClient(deployment);
        }
        else
        {
            // Black Forest Labs provider route (FLUX): the OpenAI ImageClient can't reach
            // /providers/blackforestlabs/*, so it's called directly on the AI Services host.
            _deployment = deployment;
            _credential = credential;
            _providerUri = BuildProviderUri(endpoint, _providerRoute);
        }
    }

    /// <summary>
    /// Derives the Black Forest Labs provider URL from the resource endpoint, e.g.
    /// <c>https://acct.openai.azure.com/</c> → <c>https://acct.services.ai.azure.com/providers/blackforestlabs/v1/{route}?api-version=preview</c>.
    /// </summary>
    private static string BuildProviderUri(string endpoint, string route)
    {
        var account = new Uri(endpoint).Host.Split('.')[0];
        return $"https://{account}.services.ai.azure.com/providers/blackforestlabs/v1/{route}?api-version=preview";
    }

    #endregion

    #region Methods

    /// <summary>Renders a composed prompt into PNG bytes.</summary>
    /// <param name="prompt">The image-generation prompt (from <see cref="VisionPromptComposer"/>).</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    /// <returns>The rendered image as PNG bytes.</returns>
    public Task<byte[]> RenderAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return _providerRoute is null
            ? RenderOpenAiAsync(prompt, cancellationToken)
            : RenderProviderAsync(prompt, cancellationToken);
    }

    /// <summary>gpt-image render via the OpenAI Image API. Quality is sent only when configured.</summary>
    private async Task<byte[]> RenderOpenAiAsync(string prompt, CancellationToken cancellationToken)
    {
        var options = new ImageGenerationOptions { Size = GeneratedImageSize.W1024xH1024 };
        if (_quality is not null)
            options.Quality = new GeneratedImageQuality(_quality);

        var image = await _images!.GenerateImageAsync(prompt, options, cancellationToken).ConfigureAwait(false);
        var bytes = image.Value.ImageBytes
            ?? throw new InvalidOperationException("Image generation returned no bytes.");
        return bytes.ToArray();
    }

    /// <summary>
    /// FLUX render via the Black Forest Labs provider API — a synchronous POST returning
    /// <c>{ data: [ { b64_json } ] }</c>, authenticated with the same Entra credential.
    /// </summary>
    private async Task<byte[]> RenderProviderAsync(string prompt, CancellationToken cancellationToken)
    {
        var token = await _credential!.GetTokenAsync(new TokenRequestContext(CognitiveScope), cancellationToken).ConfigureAwait(false);

        // Buffer the body as a string so a Content-Length header is set — the BFL provider endpoint
        // rejects the chunked (Transfer-Encoding) requests that JsonContent.Create would produce.
        var payload = JsonSerializer.Serialize(new { model = _deployment, prompt, width = 1024, height = 1024 });
        using var request = new HttpRequestMessage(HttpMethod.Post, _providerUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"FLUX render failed ({(int)response.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0
            && data[0].TryGetProperty("b64_json", out var b64El)
            && b64El.GetString() is { } b64)
        {
            return Convert.FromBase64String(b64);
        }

        // 200 but no image (e.g. a soft-moderated empty `data: []`) — surface the body so the cause is visible.
        var body = json.Length > 400 ? json[..400] + "…" : json;
        throw new InvalidOperationException($"FLUX render returned 200 with no image (body: {body})");
    }

    #endregion
}
