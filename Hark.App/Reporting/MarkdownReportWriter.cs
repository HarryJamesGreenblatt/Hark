using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Hark.Core.Summarization;

namespace Hark.App.Reporting;

/// <summary>Renders a <see cref="SessionReport"/> as a single Markdown file (scene images base64-embedded).</summary>
public sealed class MarkdownReportWriter : IReportWriter
{
    public string Extension => ".md";
    public string FilterName => "Markdown";

    public Task WriteAsync(SessionReport report, string path) =>
        File.WriteAllTextAsync(path, Build(report));

    private static string Build(SessionReport report)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(report.Title);
        sb.Append('_').Append(report.Timestamp.ToString("f"));
        var facts = new List<string>();
        if (report.Speakers is { Speakers.Count: > 0 } sp) facts.Add(Plural(sp.Speakers.Count, "speaker"));
        if (report.Beats.Count > 0) facts.Add(Plural(report.Beats.Count, "vision beat"));
        foreach (var f in facts) sb.Append(" \u00b7 ").Append(f);
        sb.AppendLine("_").AppendLine();

        // Vision leads, then the recaps, then the raw transcript last — matching the HTML/Word layout.
        if (report.Beats.Count > 0) AppendVision(sb, report.Beats);
        if (report.Recap is not null) AppendRecap(sb, report.Recap);
        if (report.Speakers is { Speakers.Count: > 0 }) AppendSpeakers(sb, report.Speakers);
        if (!string.IsNullOrWhiteSpace(report.Transcript))
        {
            sb.AppendLine("## Transcript").AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(report.Transcript.TrimEnd());
            sb.AppendLine("```").AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static void AppendRecap(StringBuilder sb, MeetingRecap recap)
    {
        sb.AppendLine("## Conversation summary").AppendLine();
        if (!string.IsNullOrWhiteSpace(recap.Overview)) sb.AppendLine(recap.Overview.Trim()).AppendLine();
        foreach (var t in recap.Topics)
        {
            sb.Append("### ").AppendLine(t.Title?.Trim());
            if (!string.IsNullOrWhiteSpace(t.Summary)) sb.AppendLine(t.Summary.Trim());
            foreach (var d in t.Details) sb.Append("- ").AppendLine(d?.Trim());
            sb.AppendLine();
        }
        if (recap.FollowUps.Count > 0)
        {
            sb.AppendLine("### Follow-up tasks").AppendLine();
            foreach (var f in recap.FollowUps)
            {
                sb.Append("- [ ] ").Append(f.Task?.Trim());
                if (!string.IsNullOrWhiteSpace(f.Owner)) sb.Append(" \u2014 ").Append(f.Owner.Trim());
                sb.AppendLine();
            }
            sb.AppendLine();
        }
    }

    private static void AppendSpeakers(StringBuilder sb, SpeakerRecap recap)
    {
        sb.AppendLine("## Speakers").AppendLine();
        foreach (var s in recap.Speakers)
        {
            sb.Append("### ").AppendLine(s.Speaker?.Trim());
            if (!string.IsNullOrWhiteSpace(s.Summary)) sb.AppendLine(s.Summary.Trim());
            foreach (var p in s.Points) sb.Append("- ").AppendLine(p?.Trim());
            sb.AppendLine();
        }
    }

    private static void AppendVision(StringBuilder sb, IReadOnlyList<ReportBeat> beats)
    {
        sb.AppendLine("## Vision").AppendLine();
        int n = 1;
        foreach (var beat in beats)
        {
            sb.Append("### ").Append(n++).Append(". ").AppendLine(beat.Title?.Trim());
            foreach (var node in beat.Nodes)
            {
                sb.Append("- **").Append(node.Label?.Trim()).Append("**");
                if (!string.IsNullOrWhiteSpace(node.Detail)) sb.Append(" \u2014 ").Append(node.Detail.Trim());
                sb.AppendLine();
            }
            if (beat.Nodes.Count > 0) sb.AppendLine();
            if (beat.Scene is not null)
                sb.Append("![scene](data:image/png;base64,")
                  .Append(Convert.ToBase64String(beat.Scene)).AppendLine(")").AppendLine();
        }
    }
}
