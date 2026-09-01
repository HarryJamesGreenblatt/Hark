using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hark.Core.Summarization;
using Hark.Oracle.Vision;

namespace Hark.App.Reporting;

/// <summary>One vision beat as report data: its diagram title, nodes, and scene image bytes (if any).</summary>
public sealed record ReportBeat(string Title, IReadOnlyList<InfographicNode> Nodes, byte[]? Scene);

/// <summary>
/// A format-agnostic snapshot of a session — transcript, the Conversation and Speakers recaps, and the
/// vision slideshow — that an <see cref="IReportWriter"/> renders to one concrete file format.
/// </summary>
public sealed record SessionReport(
    string Title,
    DateTime Timestamp,
    string Transcript,
    MeetingRecap? Recap,
    SpeakerRecap? Speakers,
    IReadOnlyList<ReportBeat> Beats);

/// <summary>Writes a <see cref="SessionReport"/> to a file in one concrete format.</summary>
public interface IReportWriter
{
    /// <summary>The file extension including the dot, e.g. <c>.md</c>.</summary>
    string Extension { get; }
    /// <summary>A human label for the save-dialog filter, e.g. "Markdown".</summary>
    string FilterName { get; }
    /// <summary>Renders <paramref name="report"/> to <paramref name="path"/>.</summary>
    Task WriteAsync(SessionReport report, string path);
}

/// <summary>Maps the diagram colour words to hex, shared by the markup writers (no WPF dependency).</summary>
public static class ReportPalette
{
    public static string Hex(string? word) => (word?.Trim().ToLowerInvariant()) switch
    {
        "green" => "#22C55E",
        "orange" => "#F59E0B",
        "purple" => "#A855F7",
        "red" => "#EF4444",
        _ => "#3B82F6",   // blue default
    };
}
