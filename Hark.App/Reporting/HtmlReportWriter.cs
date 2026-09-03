using System;
using System.Collections.Generic;
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
        File.WriteAllTextAsync(path, Build(report, transcriptOpen: false, lightMode: false));

    /// <summary>Renders the report HTML as a string; <paramref name="transcriptOpen"/> expands the transcript
    /// (used by the PDF export, where a collapsed <c>&lt;details&gt;</c> can't be opened) and
    /// <paramref name="lightMode"/> swaps to a light printable palette (used by the PDF export, so the dark
    /// theme doesn't clash with WebView2's white page margin).</summary>
    internal static string Render(SessionReport report, bool transcriptOpen, bool lightMode) => Build(report, transcriptOpen, lightMode);

    private static string Build(SessionReport report, bool transcriptOpen, bool lightMode)
    {
        var stamp = Esc(report.Timestamp.ToString("f"));
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>Hark report \u2014 ").Append(stamp).AppendLine("</title>");
        sb.Append("<style>").Append(Css);
        if (lightMode) sb.Append(LightCss);
        sb.AppendLine("</style></head><body><main class=\"wrap\">");

        // Hero — an Oracle-eye mark, the "session report" eyebrow, the title, and a meta line of counts.
        sb.AppendLine("<header class=\"hero\">");
        if (report.Icon is { Length: > 0 } icon)
            sb.Append("<img class=\"eye\" alt=\"\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(icon)).AppendLine("\">");
        else
            sb.AppendLine("<div class=\"eye\" aria-hidden=\"true\"></div>");
        sb.AppendLine("<div class=\"hero-text\"><div class=\"brand\">HARK \u00b7 session report</div>");
        sb.Append("<h1>").Append(Esc(report.Title)).AppendLine("</h1>");
        sb.Append("<div class=\"meta\">").Append(stamp);
        var facts = new List<string>();
        if (report.Speakers is { Speakers.Count: > 0 } sp) facts.Add(Plural(sp.Speakers.Count, "speaker"));
        if (report.Beats.Count > 0) facts.Add(Plural(report.Beats.Count, "vision beat"));
        foreach (var f in facts) sb.Append(" <span class=\"sep\">\u00b7</span> ").Append(Esc(f));
        sb.AppendLine("</div></div></header>");

        // Vision leads — it's the flagship. Then the recap, the speakers, and the raw transcript last.
        if (report.Beats.Count > 0) AppendVision(sb, report);
        if (report.Recap is not null) AppendRecap(sb, report.Recap);
        if (report.Speakers is { Speakers.Count: > 0 }) AppendSpeakers(sb, report.Speakers);
        if (!string.IsNullOrWhiteSpace(report.Transcript)) AppendTranscript(sb, report.Transcript, transcriptOpen);

        sb.AppendLine("</main></body></html>");
        return sb.ToString();
    }

    private static void SectionHead(StringBuilder sb, string label) =>
        sb.Append("<div class=\"sec-head\"><span class=\"tick\"></span><h2>").Append(Esc(label)).AppendLine("</h2></div>");

    private static void AppendRecap(StringBuilder sb, MeetingRecap recap)
    {
        sb.AppendLine("<section>");
        SectionHead(sb, "Conversation summary");
        if (!string.IsNullOrWhiteSpace(recap.Overview))
            sb.Append("<p class=\"lead\">").Append(Esc(recap.Overview.Trim())).AppendLine("</p>");
        foreach (var t in recap.Topics)
        {
            sb.AppendLine("<article class=\"topic\">");
            sb.Append("<h3>").Append(Esc(t.Title?.Trim())).AppendLine("</h3>");
            if (!string.IsNullOrWhiteSpace(t.Summary)) sb.Append("<p>").Append(Esc(t.Summary.Trim())).AppendLine("</p>");
            if (t.Details.Count > 0)
            {
                sb.AppendLine("<ul>");
                foreach (var d in t.Details) sb.Append("<li>").Append(Esc(d?.Trim())).AppendLine("</li>");
                sb.AppendLine("</ul>");
            }
            sb.AppendLine("</article>");
        }
        if (recap.FollowUps.Count > 0)
        {
            sb.AppendLine("<h3 class=\"followups-h\">Follow-up tasks</h3><ul class=\"tasks\">");
            foreach (var f in recap.FollowUps)
            {
                sb.Append("<li><span class=\"box\"></span><span>").Append(Esc(f.Task?.Trim()));
                if (!string.IsNullOrWhiteSpace(f.Owner))
                    sb.Append(" <span class=\"owner\">").Append(Esc(f.Owner.Trim())).Append("</span>");
                sb.AppendLine("</span></li>");
            }
            sb.AppendLine("</ul>");
        }
        sb.AppendLine("</section>");
    }

    private static void AppendSpeakers(StringBuilder sb, SpeakerRecap recap)
    {
        sb.AppendLine("<section>");
        SectionHead(sb, "Speakers");
        foreach (var s in recap.Speakers)
        {
            var name = s.Speaker?.Trim() ?? string.Empty;
            sb.AppendLine("<article class=\"speaker\">");
            sb.Append("<div class=\"avatar\">").Append(Esc(Initials(name))).AppendLine("</div>");
            sb.AppendLine("<div class=\"speaker-body\">");
            sb.Append("<h3>").Append(Esc(name)).AppendLine("</h3>");
            if (!string.IsNullOrWhiteSpace(s.Summary)) sb.Append("<p>").Append(Esc(s.Summary.Trim())).AppendLine("</p>");
            if (s.Points.Count > 0)
            {
                sb.AppendLine("<ul>");
                foreach (var p in s.Points) sb.Append("<li>").Append(Esc(p?.Trim())).AppendLine("</li>");
                sb.AppendLine("</ul>");
            }
            sb.AppendLine("</div></article>");
        }
        sb.AppendLine("</section>");
    }

    // The vision "beat card" — the reusable layout primitive: the mind-map (title + coloured nodes) on the
    // left, its scene image beside it on the right, kept together. This is the shape the DOCX/PPTX writers mirror.
    private static void AppendVision(StringBuilder sb, SessionReport report)
    {
        sb.AppendLine("<section>");
        SectionHead(sb, "Vision");
        int n = 1;
        foreach (var beat in report.Beats)
        {
            var hasScene = beat.Scene is not null;
            sb.Append("<article class=\"beat").Append(hasScene ? string.Empty : " no-scene").AppendLine("\">");

            sb.AppendLine("<div class=\"beat-main\">");
            sb.Append("<h3 class=\"beat-title\"><span class=\"beat-num\">").Append(n++).Append("</span>")
              .Append(Esc(beat.Title?.Trim())).AppendLine("</h3>");
            if (beat.Nodes.Count > 0)
            {
                sb.AppendLine("<ul class=\"nodes\">");
                foreach (var node in beat.Nodes)
                {
                    sb.Append("<li><span class=\"node-dot\" style=\"background:").Append(ReportPalette.Hex(node.Color))
                      .Append("\"></span><span class=\"node-label\">").Append(Esc(node.Label?.Trim())).Append("</span>");
                    if (!string.IsNullOrWhiteSpace(node.Detail))
                        sb.Append("<span class=\"node-detail\">").Append(Esc(node.Detail.Trim())).Append("</span>");
                    sb.AppendLine("</li>");
                }
                sb.AppendLine("</ul>");
            }
            sb.AppendLine("</div>");

            if (hasScene)
                sb.Append("<figure class=\"beat-figure\"><img alt=\"scene\" src=\"data:image/png;base64,")
                  .Append(Convert.ToBase64String(beat.Scene!)).AppendLine("\"></figure>");

            sb.AppendLine("</article>");
        }
        sb.AppendLine("</section>");
    }

    private static void AppendTranscript(StringBuilder sb, string transcript, bool open)
    {
        sb.AppendLine("<section>");
        SectionHead(sb, "Transcript");
        sb.Append("<details class=\"transcript\"").Append(open ? " open" : string.Empty)
          .Append("><summary>Full transcript</summary><pre>")
          .Append(Esc(transcript.TrimEnd())).AppendLine("</pre></details>");
        sb.AppendLine("</section>");
    }

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Up to two initials for a speaker avatar (first + last token).</summary>
    private static string Initials(string name)
    {
        var parts = name.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }

    private const string Css = """
    :root{--bg:#0B0D10;--surface:#12151A;--surface-2:#181C22;--border:#252C36;--text:#E6EAEF;--dim:#9AA2AC;--faint:#697079;--accent:#FF4B3E;--r:14px;--r-sm:10px;--measure:900px;--lead:#CFD5DC}
    *{box-sizing:border-box}
    body{margin:0;background:var(--bg);color:var(--text);font-family:'Segoe UI Variable','Segoe UI',system-ui,-apple-system,sans-serif;font-size:15px;line-height:1.55;background-image:radial-gradient(1200px 520px at 50% -240px,rgba(255,75,62,.07),transparent 70%)}
    ::selection{background:rgba(255,75,62,.32)}
    .wrap{max-width:var(--measure);margin:0 auto;padding:56px 28px 110px}
    .hero{display:flex;align-items:center;gap:18px;padding-bottom:26px;border-bottom:1px solid var(--border)}
    .eye{width:38px;height:38px;border-radius:50%;flex:none;background:radial-gradient(circle at 50% 38%,#FF7A6B 0%,#F0362A 42%,#6E0E08 100%);box-shadow:0 0 22px rgba(255,70,55,.5),inset 0 0 6px rgba(0,0,0,.45)}
    img.eye{object-fit:cover}
    .brand{font-size:11px;letter-spacing:.24em;text-transform:uppercase;color:var(--faint);margin-bottom:6px}
    h1{font-size:27px;line-height:1.2;margin:0;font-weight:650;letter-spacing:-.01em}
    .meta{color:var(--dim);font-size:13px;margin-top:8px}
    .meta .sep{color:var(--faint);margin:0 3px}
    section{margin:48px 0}
    .sec-head{display:flex;align-items:center;gap:10px;margin:0 0 20px}
    .sec-head .tick{width:22px;height:2px;border-radius:2px;background:var(--accent)}
    .sec-head h2{font-size:12px;letter-spacing:.18em;text-transform:uppercase;color:var(--dim);margin:0;font-weight:600}
    h3{font-size:15px;margin:0 0 6px;font-weight:640}
    p{margin:6px 0}
    .lead{font-size:16px;color:var(--lead);margin:0 0 22px;max-width:70ch}
    ul{margin:8px 0;padding-left:18px}li{margin:4px 0}
    .topic{background:var(--surface);border:1px solid var(--border);border-left:2px solid var(--accent);border-radius:var(--r-sm);padding:14px 18px;margin:12px 0;break-inside:avoid}
    .topic h3{margin-bottom:4px}
    .followups-h{margin:28px 0 10px}
    ul.tasks{list-style:none;padding:0;margin:8px 0}
    ul.tasks li{display:flex;align-items:flex-start;gap:11px;margin:8px 0}
    ul.tasks .box{width:15px;height:15px;border:1.5px solid var(--faint);border-radius:4px;flex:none;margin-top:3px}
    ul.tasks .owner{color:var(--accent);font-size:12.5px;font-weight:600;margin-left:5px;white-space:nowrap}
    .speaker{display:flex;gap:14px;background:var(--surface);border:1px solid var(--border);border-radius:var(--r);padding:16px 18px;margin:12px 0;break-inside:avoid}
    .avatar{width:38px;height:38px;border-radius:50%;flex:none;display:flex;align-items:center;justify-content:center;font-size:13px;font-weight:700;color:#fff;background:linear-gradient(135deg,#3B82F6,#A855F7)}
    .speaker-body{min-width:0}
    .speaker h3{margin-bottom:3px}
    .beat{display:grid;grid-template-columns:1fr minmax(210px,300px);gap:26px;align-items:start;background:var(--surface);border:1px solid var(--border);border-radius:var(--r);padding:20px 22px;margin:16px 0;break-inside:avoid}
    .beat.no-scene{grid-template-columns:1fr}
    .beat-main{min-width:0}
    .beat-title{display:flex;align-items:center;gap:11px;font-size:16px;margin:0 0 12px}
    .beat-num{width:24px;height:24px;border-radius:50%;flex:none;display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:700;color:var(--dim);background:var(--surface-2);border:1px solid var(--border);font-variant-numeric:tabular-nums}
    ul.nodes{list-style:none;padding:0;margin:0}
    ul.nodes li{display:flex;align-items:baseline;gap:9px;margin:8px 0;flex-wrap:wrap}
    .node-dot{width:9px;height:9px;border-radius:50%;flex:none;align-self:center}
    .node-label{font-weight:640;font-size:14px}
    .node-detail{color:var(--dim);font-size:13.5px}
    .beat-figure{margin:0;align-self:center}
    .beat-figure img{width:100%;display:block;border-radius:var(--r-sm);border:1px solid var(--border)}
    .transcript summary{cursor:pointer;color:var(--dim);font-size:13px;user-select:none;padding:10px 14px;background:var(--surface);border:1px solid var(--border);border-radius:var(--r-sm)}
    .transcript[open] summary{border-bottom-left-radius:0;border-bottom-right-radius:0}
    .transcript pre{white-space:pre-wrap;word-wrap:break-word;background:var(--surface);border:1px solid var(--border);border-top:0;border-radius:0 0 var(--r-sm) var(--r-sm);padding:16px;font-family:inherit;font-size:13px;color:var(--dim);margin:0;max-height:480px;overflow:auto}
    @media(max-width:620px){.beat{grid-template-columns:1fr}.wrap{padding:40px 18px 80px}}
    @media print{.beat,.topic,.speaker{break-inside:avoid}}
    """;

    /// <summary>Light-mode palette override, appended after <see cref="Css"/> for the PDF export so the
    /// printed page sits on a light surface (no dark content clashing with the white page margin).</summary>
    private const string LightCss = """
    :root{--bg:#FFFFFF;--surface:#F5F7F9;--surface-2:#EDF1F4;--border:#DCE1E7;--text:#1A1D21;--dim:#586069;--faint:#8A929B;--lead:#3C444D}
    body{background-image:none}
    """;

    /// <summary>XML/HTML-escapes a string for safe insertion as element text or an attribute value.</summary>
    private static string Esc(string? s) =>
        System.Security.SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;
}
