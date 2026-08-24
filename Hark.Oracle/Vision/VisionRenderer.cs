using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Images;

namespace Hark.Oracle.Vision;

/// <summary>
/// The image backend for <c>Oracle.Vision</c> — a thin, keyless wrapper over an Azure OpenAI
/// <c>gpt-image</c> deployment (the analog of sequitur's <c>ImageStudio</c>, native in .NET). Renders a
/// composed prompt into PNG bytes.
/// <para>
/// <b>Infra note:</b> requires a <c>gpt-image-1</c> image deployment on the Azure OpenAI resource
/// (separate from the chat deployment the recap / concept calls use). The concept tier works without it;
/// this tier is exercised only once the image model is provisioned.
/// </para>
/// </summary>
public sealed class VisionRenderer
{
    #region Fields

    /// <summary>The image client bound to the configured gpt-image deployment.</summary>
    private readonly ImageClient _images;

    #endregion

    #region Constructor(s)

    /// <summary>Creates a renderer bound to an Azure OpenAI image deployment.</summary>
    /// <param name="endpoint">The resource endpoint, e.g. <c>https://my-aoai.openai.azure.com/</c>.</param>
    /// <param name="deployment">The image model deployment name (e.g. a <c>gpt-image-1</c> deployment).</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public VisionRenderer(string endpoint, string deployment, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment);

        var client = new AzureOpenAIClient(new Uri(endpoint), credential ?? new DefaultAzureCredential());
        _images = client.GetImageClient(deployment);
    }

    #endregion

    #region Methods

    /// <summary>Renders a composed prompt into PNG bytes.</summary>
    /// <param name="prompt">The image-generation prompt (from <see cref="VisionPromptComposer"/>).</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    /// <returns>The rendered image as PNG bytes.</returns>
    public async Task<byte[]> RenderAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        // Square canvas for the crystal ball; other aspects deferred until the surface needs them.
        var options = new ImageGenerationOptions { Size = GeneratedImageSize.W1024xH1024 };

        var image = await _images.GenerateImageAsync(prompt, options, cancellationToken).ConfigureAwait(false);
        var bytes = image.Value.ImageBytes
            ?? throw new InvalidOperationException("Image generation returned no bytes.");
        return bytes.ToArray();
    }

    #endregion
}
