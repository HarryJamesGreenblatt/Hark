using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Hark.Core.Summarization;

namespace Hark.App.Reporting;

/// <summary>Renders a <see cref="SessionReport"/> as one self-contained HTML file (images base64-embedded).</summary>
public sealed class HtmlReportWriter : IReportWriter
{
    public string Extension => ".html";
    public string FilterName => "Web page";

    public Task WriteAsync(SessionReport report, string path) =>
        File.WriteAllTextAsync(path, Build(report));

    private static string Build(SessionReport report)
    {
        var stamp = Esc(report.Timestamp.ToString("f"));
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>Hark report \u2014 ").Append(stamp).AppendLine("</title><style>");
        sb.AppendLine("body{font-family:'Segoe UI Variable','Segoe UI',system-ui,sans-serif;background:#0B0D10;color:#DEE3E8;margin:0;padding:32px;line-height:1.5}");
        sb.AppendLine("h1{font-size:22px;margin:0 0 4px}.sub{color:#8A8F96;font-size:13px;margin-bottom:28px}");
        sb.AppendLine("h2{font-size:16px;color:#C8CDD4;border-bottom:1px solid #23272E;padding-bottom:6px;margin-top:36px}");
        sb.AppendLine("h3{font-size:14px;margin:18px 0 6px}");
        sb.AppendLine("pre{white-space:pre-wrap;word-wrap:break-word;background:#111418;border:1px solid #1E232A;border-radius:8px;padding:14px;font-family:inherit;font-size:13px}");
        sb.AppendLine("ul{margin:6px 0}li{margin:3px 0}p{margin:6px 0}");
        sb.AppendLine(".beat{background:#111418;border:1px solid #1E232A;border-radius:10px;padding:16px;margin:16px 0}");
        sb.AppendLine(".beat img{max-width:360px;width:100%;border-radius:8px;margin-top:10px;display:block}");
        sb.AppendLine(".chips{margin:6px 0}.chip{display:inline-block;border-radius:12px;padding:3px 10px;margin:3px 6px 3px 0;font-size:12px;font-weight:600;color:#fff}");
        sb.AppendLine(".detail{color:#9AA0A6;font-size:12px;margin:2px 0 8px 2px}");
        sb.AppendLine("</style></head><body>");
        sb.Append("<h1>").Append(Esc(report.Title)).Append("</h1><div class=\"sub\">").Append(stamp).AppendLine("</div>");

        if (!string.IsNullOrWhiteSpace(report.Transcript))
            sb.Append("<h2>Transcript</h2><pre>").Append(Esc(report.Transcript)).AppendLine("</pre>");
        if (report.Recap is not null) AppendRecap(sb, report.Recap);
        if (report.Speakers is { Speakers.Count: > 0 }) AppendSpeakers(sb, report.Speakers);
        if (report.Beats.Count > 0) AppendVision(sb, report);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendRecap(StringBuilder sb, MeetingRecap recap)
    {
        sb.AppendLine("<h2>Conversation summary</h2>");
        if (!string.IsNullOrWhiteSpace(recap.Overview))
            sb.Append("<p>").Append(Esc(recap.Overview.Trim())).AppendLine("</p>");
        foreach (var t in recap.Topics)
        {
            sb.Append("<h3>").Append(Esc(t.Title?.Trim())).AppendLine("</h3>");
            if (!string.IsNullOrWhiteSpace(t.Summary)) sb.Append("<p>").Append(Esc(t.Summary.Trim())).AppendLine("</p>");
            if (t.Details.Count > 0)
            {
                sb.AppendLine("<ul>");
                foreach (var d in t.Details) sb.Append("<li>").Append(Esc(d?.Trim())).AppendLine("</li>");
                sb.AppendLine("</ul>");
            }
        }
        if (recap.FollowUps.Count > 0)
        {
            sb.AppendLine("<h3>Follow-up tasks</h3><ul>");
            foreach (var f in recap.FollowUps)
            {
                sb.Append("<li>").Append(Esc(f.Task?.Trim()));
                if (!string.IsNullOrWhiteSpace(f.Owner)) sb.Append(" \u2014 ").Append(Esc(f.Owner.Trim()));
                sb.AppendLine("</li>");
            }
            sb.AppendLine("</ul>");
        }
    }

    private static void AppendSpeakers(StringBuilder sb, SpeakerRecap recap)
    {
        sb.AppendLine("<h2>Speakers</h2>");
        foreach (var s in recap.Speakers)
        {
            sb.Append("<h3>").Append(Esc(s.Speaker?.Trim())).AppendLine("</h3>");
            if (!string.IsNullOrWhiteSpace(s.Summary)) sb.Append("<p>").Append(Esc(s.Summary.Trim())).AppendLine("</p>");
            if (s.Points.Count > 0)
            {
                sb.AppendLine("<ul>");
                foreach (var p in s.Points) sb.Append("<li>").Append(Esc(p?.Trim())).AppendLine("</li>");
                sb.AppendLine("</ul>");
            }
        }
    }

    private static void AppendVision(StringBuilder sb, SessionReport report)
    {
        sb.AppendLine("<h2>Vision slideshow</h2>");
        int n = 1;
        foreach (var beat in report.Beats)
        {
            sb.AppendLine("<div class=\"beat\">");
            sb.Append("<h3>").Append(n++).Append(". ").Append(Esc(beat.Title?.Trim())).AppendLine("</h3>");
            if (beat.Nodes.Count > 0)
            {
                sb.AppendLine("<div class=\"chips\">");
                foreach (var node in beat.Nodes)
                    sb.Append("<span class=\"chip\" style=\"background:").Append(ReportPalette.Hex(node.Color))
                      .Append("\">").Append(Esc(node.Label?.Trim())).AppendLine("</span>");
                sb.AppendLine("</div>");
                foreach (var node in beat.Nodes)
                    if (!string.IsNullOrWhiteSpace(node.Detail))
                        sb.Append("<div class=\"detail\"><b>").Append(Esc(node.Label?.Trim()))
                          .Append(":</b> ").Append(Esc(node.Detail.Trim())).AppendLine("</div>");
            }
            if (beat.Scene is not null)
                sb.Append("<img alt=\"scene\" src=\"data:image/png;base64,")
                  .Append(Convert.ToBase64String(beat.Scene)).AppendLine("\">");
            sb.AppendLine("</div>");
        }
    }

    /// <summary>XML/HTML-escapes a string for safe insertion as element text or an attribute value.</summary>
    private static string Esc(string? s) =>
        System.Security.SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;
}
