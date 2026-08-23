using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Chat;

namespace Hark.Core.Transcription;

/// <summary>
/// Recognition (Azure OpenAI) — a text-only <em>semantic</em> second pass over already-diarized
/// segments. Acoustic diarization clusters on voice embeddings alone and so (a) splits one continuous
/// speaker into several labels and (b) swaps similar speakers. This refiner reads the transcript's
/// <em>content</em> — first-person continuity, who is addressed, question/answer structure — and
/// reassigns each utterance to its true speaker, an axis of evidence orthogonal to acoustics.
/// <para>
/// It is strictly non-destructive: the model returns only an <c>index → speaker</c> map; the segment
/// <b>text, offset and duration are copied verbatim</b> (only <see cref="TranscriptSegment.SpeakerId"/>
/// can change). Empty input, a single speaker, or any failure returns the input unchanged, so the pass
/// is never worse than the acoustic result.
/// </para>
/// <para>
/// Keyless, mirroring <see cref="Summarization.AzureOpenAiSummarizer"/>: the signed-in identity must
/// hold the "Cognitive Services OpenAI User" role on the resource.
/// </para>
/// </summary>
public sealed class SemanticDiarizationRefiner
{
    #region Fields

    /// <summary>The chat client bound to the configured Azure OpenAI deployment.</summary>
    private readonly ChatClient _chat;

    /// <summary>Case-insensitive options for deserializing the model's JSON assignment map.</summary>
    private static readonly JsonSerializerOptions AssignmentJson = new() { PropertyNameCaseInsensitive = true };

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Creates a semantic refiner bound to an Azure OpenAI chat deployment.
    /// </summary>
    /// <param name="endpoint">The resource endpoint, e.g. <c>https://my-aoai.openai.azure.com/</c>.</param>
    /// <param name="deployment">The chat model deployment name.</param>
    /// <param name="credential">Optional credential override; defaults to <see cref="DefaultAzureCredential"/>.</param>
    public SemanticDiarizationRefiner(string endpoint, string deployment, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment);

        var client = new AzureOpenAIClient(new Uri(endpoint), credential ?? new DefaultAzureCredential());
        _chat = client.GetChatClient(deployment);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Re-attributes the given diarized segments using conversational coherence, returning the same
    /// segments with text/timing untouched and <see cref="TranscriptSegment.SpeakerId"/> possibly remapped.
    /// </summary>
    /// <param name="segments">The acoustically-diarized segments (e.g. from <see cref="FastTranscriptionRefiner"/>).</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    /// <returns>The re-attributed segments, or the input unchanged when there is nothing to fix or on failure.</returns>
    public async Task<IReadOnlyList<TranscriptSegment>> RefineAsync(
        IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken = default)
    {
        if (segments is null || segments.Count == 0) return segments ?? Array.Empty<TranscriptSegment>();

        // Nothing to disambiguate with a single (or no) acoustic speaker.
        int acousticSpeakers = CountDistinctSpeakers(segments);
        if (acousticSpeakers <= 1) return segments;

        try
        {
            var map = await RequestAssignmentsAsync(segments, cancellationToken).ConfigureAwait(false);
            if (map.Count == 0) return segments;

            var relabeled = ApplyAssignments(segments, map);

            // Over-merge guard: if the model collapsed a clearly multi-speaker session to one, distrust it.
            if (CountDistinctSpeakers(relabeled) <= 1) return segments;

            return Canonicalize(relabeled);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Never worse than the acoustic result.
            return segments;
        }
    }

    /// <summary>Asks the model for an index→speaker remap of the segments.</summary>
    private async Task<IReadOnlyDictionary<int, string>> RequestAssignmentsAsync(
        IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(BuildIndexedTranscript(segments)),
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0f,                 // deterministic re-labeling
            MaxOutputTokenCount = 8000,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "speaker_assignments",
                BinaryData.FromString(AssignmentSchema),
                jsonSchemaIsStrict: true),
        };

        var completion = await _chat.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (completion.Value.Content.Count == 0) return EmptyMap;

        var result = JsonSerializer.Deserialize<AssignmentResult>(completion.Value.Content[0].Text, AssignmentJson);
        if (result?.Assignments is not { Count: > 0 }) return EmptyMap;

        var map = new Dictionary<int, string>(result.Assignments.Count);
        foreach (var a in result.Assignments)
        {
            if (a.Index >= 0 && a.Index < segments.Count && !string.IsNullOrWhiteSpace(a.Speaker))
                map[a.Index] = a.Speaker.Trim();
        }
        return map;
    }

    /// <summary>Applies the remap, keeping each segment's text/timing and only swapping the speaker label.</summary>
    private static IReadOnlyList<TranscriptSegment> ApplyAssignments(
        IReadOnlyList<TranscriptSegment> segments, IReadOnlyDictionary<int, string> map)
    {
        var result = new List<TranscriptSegment>(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            // Fall back to the acoustic label when the model didn't (or couldn't) reassign this index.
            var speaker = map.TryGetValue(i, out var mapped) ? mapped : seg.SpeakerId;
            result.Add(seg with { SpeakerId = speaker });
        }
        return result;
    }

    /// <summary>
    /// Normalizes whatever labels the model produced into contiguous <c>Guest-N</c> in order of first
    /// appearance, so downstream (speaker pages, recaps) stays consistent regardless of the model's naming.
    /// Blank/unattributed labels are left untouched.
    /// </summary>
    private static IReadOnlyList<TranscriptSegment> Canonicalize(IReadOnlyList<TranscriptSegment> segments)
    {
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int next = 1;

        var result = new List<TranscriptSegment>(segments.Count);
        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg.SpeakerId))
            {
                result.Add(seg);
                continue;
            }

            if (!canonical.TryGetValue(seg.SpeakerId, out var label))
            {
                label = $"Guest-{next++}";
                canonical[seg.SpeakerId] = label;
            }
            result.Add(seg with { SpeakerId = label });
        }
        return result;
    }

    /// <summary>Renders the segments as an indexed, labeled list the model reassigns from.</summary>
    private static string BuildIndexedTranscript(IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder(segments.Count * 48);
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var label = string.IsNullOrWhiteSpace(seg.SpeakerId) ? "Unknown" : seg.SpeakerId;
            sb.Append('[').Append(i).Append("] ").Append(label).Append(": ").Append(seg.Text).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Counts the distinct (non-blank) speaker labels across the segments.</summary>
    private static int CountDistinctSpeakers(IReadOnlyList<TranscriptSegment> segments)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seg in segments)
            if (!string.IsNullOrWhiteSpace(seg.SpeakerId))
                seen.Add(seg.SpeakerId);
        return seen.Count;
    }

    #endregion

    #region Nested Types

    /// <summary>One corrected attribution: the utterance index and its true speaker label.</summary>
    private sealed record Assignment(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("speaker")] string Speaker);

    /// <summary>The model's full response: a corrected speaker for every index.</summary>
    private sealed record AssignmentResult(
        [property: JsonPropertyName("assignments")] IReadOnlyList<Assignment> Assignments);

    #endregion

    #region Prompt & schema

    /// <summary>An empty remap, used when the model returns nothing usable.</summary>
    private static readonly IReadOnlyDictionary<int, string> EmptyMap = new Dictionary<int, string>();

    /// <summary>System prompt instructing the model to re-attribute utterances without touching the text.</summary>
    private const string SystemPrompt =
        "You correct speaker attribution in a diarized transcript. You are given an ordered list of " +
        "utterances, each on its own line as '[index] Guest-N: text'. The labels come from acoustic " +
        "diarization, which frequently (a) splits ONE continuous speaker across several labels and " +
        "(b) swaps two similar-sounding speakers.\n\n" +
        "Using conversational coherence — first-person continuity, who is being addressed, " +
        "question/answer structure, and turn-taking cadence — reassign each utterance to its TRUE " +
        "speaker. Reuse the same 'Guest-N' namespace and canonicalize so each real person has exactly " +
        "one label. If you are unsure about an utterance, keep the label it was given.\n\n" +
        "Rules:\n" +
        "- Do NOT change, merge, translate, or drop any text — you are only relabeling.\n" +
        "- Return exactly one assignment for every index present in the input, in order.\n" +
        "- Keep genuinely distinct speakers distinct; do not collapse everyone into one label.\n\n" +
        "Return a JSON object with an 'assignments' array of { index, speaker } for every index.";

    /// <summary>Strict JSON schema mirroring <see cref="AssignmentResult"/>, used for structured outputs.</summary>
    private const string AssignmentSchema = """
        {
          "type": "object",
          "properties": {
            "assignments": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "index": { "type": "integer" },
                  "speaker": { "type": "string" }
                },
                "required": ["index", "speaker"],
                "additionalProperties": false
              }
            }
          },
          "required": ["assignments"],
          "additionalProperties": false
        }
        """;

    #endregion
}
